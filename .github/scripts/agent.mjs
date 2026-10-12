// Code-editing agent: reads the uncle's request (+ feedback history) from the GitHub issue,
// asks Gemini for the edited files, applies them, runs build + tests, retries on failure.
//
// Outputs (paths from env):
//   SUMMARY_FILE  plain-language summary for the uncle (or a clarifying question)
//   CHANGED_FILE  newline-separated list of changed files (empty = nothing changed)
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const env = (k, d = '') => process.env[k] || d;
const MODEL = env('GEMINI_MODEL', 'gemini-2.5-flash');
const KEY = env('GEMINI_API_KEY');
const REPO = env('GITHUB_REPOSITORY');
const ISSUE = env('ISSUE_NUMBER');
const GH_TOKEN = env('GH_TOKEN');
const BUILD_CMD = env('BUILD_COMMAND', 'dotnet build --no-restore');
const TEST_CMD = env('TEST_COMMAND', 'dotnet test tests/InvoiceGen.Tests');
const SUMMARY_FILE = env('SUMMARY_FILE', 'agent-summary.txt');
const CHANGED_FILE = env('CHANGED_FILE', 'agent-changed.txt');
const MAX_ATTEMPTS = 3;
const MAX_CONTEXT_CHARS = 350_000;

const SYSTEM = `You are a careful web developer maintaining a small invoice generator for a non-technical shop owner.
You receive his request in plain words (possibly with earlier feedback) and the current source files.

Rules:
- Make the smallest change that satisfies the request. Do not refactor or restyle anything else.
- NEVER change tax / GST calculation, rounding or totals logic unless the request explicitly asks for it.
- Keep the existing code style and structure. Do not add new dependencies unless unavoidable.
- If the request is unclear, impossible, or risky, change nothing: return "files": [] and put ONE short, simple question in "summary".
- For every file you change or create, return its COMPLETE new content (not a diff, not a fragment).
- Never touch .github/, tests/, .env files, or lock files. If a request would require changing the tax or totals maths, change nothing and ask.
- "summary": 1-2 plain sentences for a non-technical person describing what changed visually or in behaviour. No code terms.

Reply with JSON only: {"summary": string, "files": [{"path": string, "content": string}]}`;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const sh = (cmd) => execSync(cmd, { encoding: 'utf8', maxBuffer: 50 * 1024 * 1024 }).trim();

function run(cmd) {
  try {
    const out = execSync(cmd + ' 2>&1', {
      encoding: 'utf8',
      maxBuffer: 50 * 1024 * 1024,
      timeout: 10 * 60 * 1000,
      shell: '/bin/bash',
    });
    return { ok: true, log: out };
  } catch (e) {
    return { ok: false, log: (e.stdout || '') + (e.stderr || '') || String(e) };
  }
}

// ---------------------------------------------------------------- GitHub

async function ghApi(p) {
  const res = await fetch(`https://api.github.com/repos/${REPO}${p}`, {
    headers: {
      Authorization: `Bearer ${GH_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'User-Agent': 'invoice-bot-agent',
    },
  });
  if (!res.ok) throw new Error(`GitHub ${p} -> ${res.status} ${await res.text()}`);
  return res.json();
}

async function conversation() {
  const issue = await ghApi(`/issues/${ISSUE}`);
  const comments = await ghApi(`/issues/${ISSUE}/comments?per_page=100`);
  let t = `ORIGINAL REQUEST:\n${issue.body || issue.title}\n`;
  for (const c of comments) {
    const who = c.user?.type === 'Bot' ? 'WHAT YOU DID EARLIER' : 'LATEST FEEDBACK FROM THE OWNER';
    t += `\n${who}:\n${c.body}\n`;
  }
  return t;
}

// ---------------------------------------------------------------- Gemini

async function gemini(system, user) {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${MODEL}:generateContent`;
  let lastErr;
  for (let i = 0; i < 3; i++) {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'x-goog-api-key': KEY },
      body: JSON.stringify({
        systemInstruction: { parts: [{ text: system }] },
        contents: [{ role: 'user', parts: [{ text: user }] }],
        generationConfig: { responseMimeType: 'application/json', temperature: 0.2, maxOutputTokens: 32768 },
      }),
    });
    if (res.status === 429 || res.status >= 500) {
      lastErr = new Error(`Gemini ${res.status}`);
      await sleep(8000 * (i + 1));
      continue;
    }
    if (!res.ok) throw new Error(`Gemini ${res.status}: ${await res.text()}`);
    const data = await res.json();
    const text = (data?.candidates?.[0]?.content?.parts || []).map((p) => p.text || '').join('');
    if (!text) throw new Error('Gemini returned no text (blocked or empty)');
    try {
      return JSON.parse(text);
    } catch (e) {
      lastErr = new Error('Gemini returned invalid JSON (output may have been cut off)');
    }
  }
  throw lastErr || new Error('Gemini failed');
}

// ---------------------------------------------------------------- files

const SKIP = /(^|\/)(wwwroot\/lib|bin|obj|\.vs|tests)\/|\.min\.(js|css)$|\.(png|jpe?g|gif|webp|ico|pdf|woff2?|ttf|eot|zip|mp4|lock)$|(^|\/)(package-lock\.json|yarn\.lock|pnpm-lock\.yaml)$|^\.github\//i;
const BLOCKED = [
  /^\.github\//,
  /^tests\//,
  /^\.git\//,
  /(^|\/)\.env/,
  /(^|\/)node_modules\//,
  /(^|\/)(package-lock\.json|yarn\.lock|pnpm-lock\.yaml)$/,
];

function safePath(p) {
  if (typeof p !== 'string' || !p || path.isAbsolute(p)) return null;
  const norm = path.posix.normalize(p.replace(/\\/g, '/'));
  if (norm === '..' || norm.startsWith('../')) return null;
  if (BLOCKED.some((r) => r.test(norm))) return null;
  return norm;
}

function listFiles() {
  return sh('git ls-files')
    .split('\n')
    .filter(Boolean)
    .filter((f) => !SKIP.test(f))
    .filter((f) => {
      try {
        if (fs.statSync(f).size > 200_000) return false;
        return !fs.readFileSync(f).subarray(0, 8000).includes(0);
      } catch {
        return false;
      }
    });
}

function renderFiles(files) {
  return files.map((f) => `=== ${f} ===\n${fs.readFileSync(f, 'utf8')}`).join('\n\n');
}

async function chooseContext(convo) {
  const all = listFiles();
  const total = all.reduce((n, f) => n + fs.statSync(f).size, 0);
  if (total <= MAX_CONTEXT_CHARS) return all;
  const r = await gemini(
    'You pick which source files are needed to implement a change request. Reply JSON only: {"files": ["path", ...]} with at most 15 paths from the list.',
    `${convo}\n\nFILES:\n${all.join('\n')}`
  );
  const picked = (r.files || []).filter((f) => all.includes(f));
  return picked.length ? picked : all.slice(0, 15);
}

function revert(created) {
  try {
    sh('git checkout -- .');
  } catch {}
  for (const p of created) {
    try {
      fs.unlinkSync(p);
    } catch {}
  }
}

function finish(summary, changed) {
  fs.writeFileSync(SUMMARY_FILE, summary.trim() + '\n');
  fs.writeFileSync(CHANGED_FILE, changed.join('\n') + (changed.length ? '\n' : ''));
  console.log('SUMMARY:', summary);
  console.log('CHANGED:', changed.join(', ') || '(none)');
}

// ---------------------------------------------------------------- main

async function main() {
  if (!KEY) throw new Error('GEMINI_API_KEY is not set');
  const convo = await conversation();
  const contextFiles = await chooseContext(convo);
  let prevProblem = '';

  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    console.log(`--- attempt ${attempt}/${MAX_ATTEMPTS}`);
    let user = `${convo}\n\nCURRENT FILES:\n${renderFiles(contextFiles)}`;
    if (prevProblem) {
      user += `\n\nYOUR PREVIOUS ATTEMPT WAS REJECTED. Fix this and try again:\n${prevProblem}`;
    }

    const res = await gemini(SYSTEM, user);
    const summary = String(res.summary || '').trim();
    const files = Array.isArray(res.files) ? res.files : [];

    if (!files.length) {
      finish(summary || 'I need a little more detail. What exactly should change?', []);
      return;
    }

    const created = [];
    const changed = [];
    let problem = '';
    for (const f of files) {
      const p = safePath(f && f.path);
      if (!p || typeof f.content !== 'string' || !f.content.length) {
        problem = `Invalid or forbidden file entry: ${JSON.stringify(f && f.path)}`;
        break;
      }
      if (fs.existsSync(p)) {
        const old = fs.readFileSync(p, 'utf8');
        if (old === f.content) continue;
        if (old.length > 400 && f.content.length < old.length * 0.5) {
          problem = `${p}: the new content is less than half the size of the original, it looks truncated. Return the COMPLETE file.`;
          break;
        }
      } else {
        created.push(p);
      }
      fs.mkdirSync(path.dirname(p), { recursive: true });
      fs.writeFileSync(p, f.content);
      changed.push(p);
    }

    if (!problem && !changed.length) {
      finish(summary || 'That already looks the way you described. Is there something else you would like changed?', []);
      return;
    }

    if (!problem) {
      for (const [name, cmd] of [['build', BUILD_CMD], ['tests', TEST_CMD]]) {
        const r = run(cmd);
        console.log(`${name}: ${r.ok ? 'ok' : 'FAILED'}`);
        if (!r.ok) {
          problem = `The ${name} step failed (${cmd}). Output (tail):\n${r.log.slice(-6000)}`;
          break;
        }
      }
    }

    if (!problem) {
      finish(summary || 'The change has been made.', changed);
      return;
    }

    console.log(problem);
    prevProblem = problem;
    revert(created);
  }

  throw new Error(`Could not produce a passing change after ${MAX_ATTEMPTS} attempts.\n${prevProblem}`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
