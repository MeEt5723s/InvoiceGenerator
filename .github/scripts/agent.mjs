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
const MODEL = env('GEMINI_MODEL', 'gemini-3.8-flash');
const KEY = env('GEMINI_API_KEY');
const FALLBACK_MODEL = env('GEMINI_FALLBACK_MODEL');
const REPO = env('GITHUB_REPOSITORY');
const ISSUE = env('ISSUE_NUMBER');
const GH_TOKEN = env('GH_TOKEN');
const TG_TOKEN = env('TELEGRAM_BOT_TOKEN');
const BUILD_CMD = env('BUILD_COMMAND', 'dotnet build --no-restore');
const TEST_CMD = env('TEST_COMMAND', 'dotnet test tests/InvoiceGen.Tests');
const SUMMARY_FILE = env('SUMMARY_FILE', 'agent-summary.txt');
const CHANGED_FILE = env('CHANGED_FILE', 'agent-changed.txt');
const FAILURE_FILE = env('FAILURE_FILE', 'agent-failure.txt');
const MAX_ATTEMPTS = 3;
const MAX_CONTEXT_CHARS = 350_000;

const SYSTEM = `You are a careful web developer maintaining a small invoice generator for a non-technical shop owner.
You receive his request in plain words (possibly with earlier feedback) and the current source files.

Rules:
- Make the smallest change that satisfies the request. Do not refactor or restyle anything else.
- NEVER change tax / GST calculation, rounding or totals logic unless the request explicitly asks for it.
- Keep the existing code style and structure. Do not add new dependencies unless unavoidable.
- If the request is unclear, impossible, or risky, change nothing: reply with only a SUMMARY line containing ONE short, simple question, and no FILE blocks.
- Never touch .github/, tests/, .env files, or lock files. If a request would require changing the tax or totals maths, change nothing and ask.
- SUMMARY: 1-2 plain sentences for a non-technical person describing what changed visually or in behaviour. No code terms.
- The owner may attach screenshots, shown to you as images and marked [Screenshot N] in the text. A screenshot shows the page as he sees it, often with circles, arrows or notes drawn on it. Use the picture together with his words to work out which part of the page he means, then find the matching code. If you cannot tell which element he means, change nothing and ask.
- To change an EXISTING file, use FIND/REPLACE blocks. The FIND text must be copied EXACTLY from the current file (same spaces, quotes, backslashes) and must appear exactly once, so include a neighbouring line if needed. Keep each FIND as short as possible (one to a few lines). Never return whole existing files.
- If the same change is needed in several places or files, give one block per place.
- To create a NEW file, use a NEWFILE block with its complete content.

Reply in PLAIN TEXT using exactly this format. No JSON, no markdown code fences, no escaping of any kind:

SUMMARY: <your plain-language summary or question>

FILE: path/to/existing/file.cs
<<<<<<< FIND
exact existing lines
=======
new lines
>>>>>>> REPLACE

NEWFILE: path/to/new/file.cs
<<<<<<< CONTENT
complete content of the new file
>>>>>>> END`;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const sh = (cmd) => execSync(cmd, { encoding: 'utf8', maxBuffer: 50 * 1024 * 1024 }).trim();

function run(cmd) {
  try {
    const out = execSync(cmd + ' 2>&1', {
      encoding: 'utf8',
      maxBuffer: 50 * 1024 * 1024,
      timeout: 10 * 60 * 1000,
      shell: process.platform === 'win32' ? undefined : '/bin/bash',
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

const IMG_RE = /\[\[image:([A-Za-z0-9_-]+)\]\]/g;
const MAX_IMAGES = 3; // only the most recent screenshots are shown to the model

async function conversation() {
  const issue = await ghApi(`/issues/${ISSUE}`);
  const comments = await ghApi(`/issues/${ISSUE}/comments?per_page=100`);
  const parts = [{ who: 'ORIGINAL REQUEST', body: issue.body || issue.title }];
  for (const c of comments) {
    parts.push({ who: c.user?.type === 'Bot' ? 'WHAT YOU DID EARLIER' : 'LATEST FEEDBACK FROM THE OWNER', body: c.body });
  }
  const all = [];
  for (const p of parts) for (const m of p.body.matchAll(IMG_RE)) all.push(m[1]);
  const keep = all.slice(-MAX_IMAGES);
  const firstKept = all.length - keep.length;
  let seen = 0;
  let t = '';
  for (const p of parts) {
    const body = p.body.replace(IMG_RE, () => {
      const idx = seen++;
      return idx < firstKept ? '[an earlier screenshot, not shown]' : `[Screenshot ${idx - firstKept + 1} attached]`;
    });
    t += `${t ? '\n' : ''}${p.who}:\n${body}\n`;
  }
  return { text: t, imageIds: keep };
}

// Download the screenshots from Telegram. Returns [{mimeType, data(base64)}] or null if one cannot be read.
const MIME = { jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp' };
async function loadImages(ids) {
  const out = [];
  for (const id of ids) {
    let img = null;
    for (let i = 0; i < 3 && !img; i++) {
      try {
        if (!TG_TOKEN) throw new Error('TELEGRAM_BOT_TOKEN is not set');
        const meta = await (await fetch(`https://api.telegram.org/bot${TG_TOKEN}/getFile?file_id=${id}`, { signal: AbortSignal.timeout(30_000) })).json();
        const fp = meta?.result?.file_path;
        if (!fp) throw new Error(`getFile failed: ${meta?.description || 'no file_path'}`);
        const mimeType = MIME[fp.split('.').pop().toLowerCase()];
        if (!mimeType) throw new Error(`unsupported picture type: ${fp}`);
        const res = await fetch(`https://api.telegram.org/file/bot${TG_TOKEN}/${fp}`, { signal: AbortSignal.timeout(60_000) });
        if (!res.ok) throw new Error(`download failed: ${res.status}`);
        const buf = Buffer.from(await res.arrayBuffer());
        if (buf.length > 8 * 1024 * 1024) throw new Error('picture is too large');
        img = { mimeType, data: buf.toString('base64') };
        console.log(`screenshot loaded: ${mimeType}, ${buf.length} bytes`);
      } catch (e) {
        console.log(`screenshot download problem (try ${i + 1}/3): ${e.message}`);
        await sleep(3000);
      }
    }
    if (!img) return null;
    out.push(img);
  }
  return out;
}

// ---------------------------------------------------------------- Gemini

// fetch that never throws: a hung or dropped connection comes back as {networkError} so the caller can retry.
async function tryFetch(url, init) {
  try {
    return await fetch(url, { ...init, signal: AbortSignal.timeout(150_000) });
  } catch (e) {
    return { networkError: (e && e.cause && e.cause.code) || (e && e.name) || String(e) };
  }
}

// Fix the usual mistakes models make inside JSON strings: raw newlines/tabs and invalid backslash escapes.
function repairJson(t) {
  let out = '';
  let inStr = false;
  for (let i = 0; i < t.length; i++) {
    const c = t[i];
    if (!inStr) {
      if (c === '"') inStr = true;
      out += c;
      continue;
    }
    if (c === '"') { inStr = false; out += c; continue; }
    if (c === '\\') {
      const n = t[i + 1];
      if (n && '"\\/bfnrt'.includes(n)) { out += c + n; i++; }
      else if (n === 'u' && /^[0-9a-fA-F]{4}$/.test(t.slice(i + 2, i + 6))) { out += t.slice(i, i + 6); i += 5; }
      else out += '\\\\';
      continue;
    }
    if (c === '\n') { out += '\\n'; continue; }
    if (c === '\r') { out += '\\r'; continue; }
    if (c === '\t') { out += '\\t'; continue; }
    out += c;
  }
  return out;
}

function parseJson(text) {
  const t = text.trim().replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '');
  const i = t.indexOf('{');
  const j = t.lastIndexOf('}');
  const body = i >= 0 && j > i ? t.slice(i, j + 1) : t;
  try {
    return JSON.parse(body);
  } catch (e1) {
    try {
      return JSON.parse(repairJson(body));
    } catch {
      throw e1;
    }
  }
}

// Plain-text reply format (see SYSTEM): SUMMARY + FILE/FIND/REPLACE blocks + NEWFILE blocks.
function parseReply(text) {
  let t = text.replace(/\r\n/g, '\n').trim();
  if (/^```/.test(t) && /```$/.test(t)) t = t.replace(/^```[^\n]*\n/, '').replace(/\n```$/, '');
  const edits = [];
  const newFiles = [];
  const editRe = /^FILE:[ \t]*(.+?)[ \t]*\n<<<<<<< FIND\n([\s\S]*?)\n=======\n([\s\S]*?)\n?>>>>>>> REPLACE[ \t]*$/gm;
  const newRe = /^NEWFILE:[ \t]*(.+?)[ \t]*\n<<<<<<< CONTENT\n([\s\S]*?)\n>>>>>>> END[ \t]*$/gm;
  for (const m of t.matchAll(editRe)) edits.push({ path: m[1].trim(), find: m[2], replace: m[3] });
  for (const m of t.matchAll(newRe)) newFiles.push({ path: m[1].trim(), content: m[2] + '\n' });
  const sm = t.match(/^SUMMARY:[ \t]*([\s\S]*?)(?=^(?:FILE|NEWFILE):|(?![\s\S]))/m);
  if (!sm && !edits.length && !newFiles.length) throw new Error('reply is not in the expected format');
  const half = /^(FILE|NEWFILE):/m.test(t) && !edits.length && !newFiles.length;
  if (half) throw new Error('FILE/NEWFILE block was not closed properly');
  return { summary: sm ? sm[1].trim() : '', edits, newFiles };
}

// Apply one find/replace to text. Exact match first; otherwise match ignoring leading/trailing spaces on each line.
function applyEdit(text, find, replace) {
  const exact = text.split(find).length - 1;
  if (exact === 1) return { text: text.replace(find, () => replace) };
  if (exact > 1) return { error: `appears ${exact} times` };
  const tl = text.split('\n');
  const fl = find.split('\n').map((x) => x.trim());
  while (fl.length && fl[0] === '') fl.shift();
  while (fl.length && fl[fl.length - 1] === '') fl.pop();
  if (!fl.length) return { error: 'was empty' };
  const hits = [];
  for (let i = 0; i + fl.length <= tl.length; i++) {
    let ok = true;
    for (let k = 0; k < fl.length; k++) {
      if (tl[i + k].trim() !== fl[k]) { ok = false; break; }
    }
    if (ok) hits.push(i);
  }
  if (hits.length !== 1) return { error: hits.length ? `appears ${hits.length} times` : 'was not found' };
  const rl = replace.split('\n');
  while (rl.length && rl[rl.length - 1] === '') rl.pop();
  return { text: [...tl.slice(0, hits[0]), ...rl, ...tl.slice(hits[0] + fl.length)].join('\n') };
}

async function gemini(system, user, mode = 'edits', images = []) {
  let lastErr;
  let useFallback = false; // set when the main model gave an unusable answer
  // Up to 8 tries. Overloaded (503) -> wait and retry. Unusable answer -> switch model right away.
  for (let i = 0; i < 8; i++) {
    const model =
      FALLBACK_MODEL && (useFallback || (i >= 3 && i % 2 === 1)) ? FALLBACK_MODEL : MODEL;
    const url = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`;
    const res = await tryFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'x-goog-api-key': KEY },
      body: JSON.stringify({
        systemInstruction: { parts: [{ text: system }] },
        contents: [{ role: 'user', parts: [{ text: user }, ...images.map((im) => ({ inlineData: { mimeType: im.mimeType, data: im.data } }))] }],
        generationConfig: mode === 'json'
          ? { responseMimeType: 'application/json', temperature: 0.2, maxOutputTokens: 32768 }
          : { temperature: 0.2, maxOutputTokens: 32768 },
      }),
    });
    if (res.networkError || res.status === 429 || res.status >= 500) {
      lastErr = new Error(
        res.networkError
          ? `Gemini request failed (${model}): ${res.networkError}`
          : `Gemini ${res.status} (${model}): ${(await res.text()).slice(0, 200)}`
      );
      console.log(`${lastErr.message} - waiting before retry ${i + 1}/8`);
      if (model === FALLBACK_MODEL) useFallback = false; // give the main model another go
      if (i < 7) await sleep(Math.min(15000 * 2 ** i, 120000));
      continue;
    }
    if (!res.ok) throw new Error(`Gemini ${res.status} (${model}): ${await res.text()}`);
    const data = await res.json();
    const cand = data?.candidates?.[0];
    const text = (cand?.content?.parts || []).map((p) => p.text || '').join('');
    let parsed;
    try {
      parsed = mode === 'json' ? parseJson(text) : parseReply(text);
    } catch (pe) {
      lastErr = new Error(`Gemini answer unusable (${model}): ${pe.message}; finishReason=${cand?.finishReason || data?.promptFeedback?.blockReason || 'unknown'}, length=${text.length}`);
      console.log(`${lastErr.message}\nSTART: ${text.slice(0, 200)}\nEND: ${text.slice(-200)}\n - retrying ${i + 1}/8`);
      if (model === MODEL && FALLBACK_MODEL) useFallback = true;
      await sleep(5000);
      continue;
    }
    return parsed;
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
    `${convo}\n\nFILES:\n${all.join('\n')}`,
    'json'
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
  const { text: convo, imageIds } = await conversation();
  const images = await loadImages(imageIds);
  if (images === null) {
    finish('I could not open the screenshot you sent. Could you send it again, as a normal photo?', []);
    return;
  }
  const contextFiles = await chooseContext(convo);
  let prevProblem = '';

  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    console.log(`--- attempt ${attempt}/${MAX_ATTEMPTS}`);
    let user = `${convo}\n\nCURRENT FILES:\n${renderFiles(contextFiles)}`;
    if (prevProblem) {
      user += `\n\nYOUR PREVIOUS ATTEMPT WAS REJECTED. Fix this and try again:\n${prevProblem}`;
    }

    const res = await gemini(SYSTEM, user, 'edits', images);
    const summary = String(res.summary || '').trim();
    const edits = Array.isArray(res.edits) ? res.edits : [];
    // "files" (whole-file replies) is still accepted as a fallback.
    const newFiles = [...(Array.isArray(res.newFiles) ? res.newFiles : []), ...(Array.isArray(res.files) ? res.files : [])];

    if (!edits.length && !newFiles.length) {
      finish(summary || 'I need a little more detail. What exactly should change?', []);
      return;
    }

    const created = [];
    const changed = [];
    let problem = '';

    // 1) targeted edits to existing files
    const pending = new Map(); // path -> { crlf, text }
    for (const e of edits) {
      const p = safePath(e && e.path);
      if (!p || !fs.existsSync(p) || typeof e.find !== 'string' || !e.find.length || typeof e.replace !== 'string') {
        problem = `Invalid edit (file must exist, "find" and "replace" must be text): ${JSON.stringify(e && e.path)}`;
        break;
      }
      if (!pending.has(p)) {
        const raw = fs.readFileSync(p, 'utf8');
        pending.set(p, { crlf: raw.includes('\r\n'), text: raw.replace(/\r\n/g, '\n') });
      }
      const st = pending.get(p);
      const r = applyEdit(st.text, e.find.replace(/\r\n/g, '\n'), e.replace.replace(/\r\n/g, '\n'));
      if (r.error) {
        problem = `${p}: the FIND text ${r.error} in the file. It must match exactly once. Copy it exactly from the current file and add a neighbouring line to make it unique. The text was:\n${e.find.slice(0, 400)}`;
        break;
      }
      st.text = r.text;
    }
    if (!problem) {
      for (const [p, st] of pending) {
        fs.writeFileSync(p, st.crlf ? st.text.replace(/\n/g, '\r\n') : st.text);
        changed.push(p);
      }
    }

    // 2) new files (or whole-file replacement as a fallback)
    for (const f of problem ? [] : newFiles) {
      const p = safePath(f && f.path);
      if (!p || typeof f.content !== 'string' || !f.content.length) {
        problem = `Invalid or forbidden file entry: ${JSON.stringify(f && f.path)}`;
        break;
      }
      if (fs.existsSync(p)) {
        const old = fs.readFileSync(p, 'utf8');
        if (old === f.content) continue;
        if (old.length > 400 && f.content.length < old.length * 0.5) {
          problem = `${p}: the new content is less than half the size of the original, it looks truncated. Use small edits instead.`;
          break;
        }
      } else {
        created.push(p);
      }
      fs.mkdirSync(path.dirname(p), { recursive: true });
      fs.writeFileSync(p, f.content);
      if (!changed.includes(p)) changed.push(p);
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
  // Tell the workflow whether this was Google being busy (not the request's fault).
  try {
    if (/^Gemini/.test(String(err && err.message))) fs.writeFileSync(FAILURE_FILE, 'busy\n');
  } catch {}
  process.exit(1);
});
