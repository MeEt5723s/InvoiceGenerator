#!/usr/bin/env bash
# Usage: wait-render.sh <render_service_id> <commit_sha> [timeout_minutes]
# Waits until Render reports a LIVE deploy of exactly this commit.
# Needs env: RENDER_API_KEY
set -uo pipefail

service="$1"
sha="$2"
timeout_min="${3:-25}"
deadline=$(( $(date +%s) + timeout_min * 60 ))
api_errors=0
triggered=0
start=$(date +%s)
trigger_after="${TRIGGER_AFTER_SEC:-240}"

while [ "$(date +%s)" -lt "$deadline" ]; do
  resp=$(curl -sS -w $'\n%{http_code}' -H "Authorization: Bearer ${RENDER_API_KEY}" -H "Accept: application/json" \
    "https://api.render.com/v1/services/${service}/deploys?limit=10" 2>&1 || true)
  code="${resp##*$'\n'}"
  json="${resp%$'\n'*}"

  # The Render API itself is refusing us (bad key, wrong service id...): say so instead of waiting in silence.
  if [ "$code" != "200" ]; then
    api_errors=$((api_errors + 1))
    echo "Render API problem (HTTP ${code}): $(echo "$json" | head -c 200)"
    if [ "$api_errors" -ge 5 ]; then
      echo "Giving up: check the RENDER_API_KEY secret and the STAGING_SERVICE_ID variable."
      exit 1
    fi
    sleep 20
    continue
  fi
  api_errors=0

  status=$(echo "$json" | jq -r --arg sha "$sha" \
    '[.[] | (.deploy // .) | select(.commit.id == $sha)] | .[0].status // "none"' 2>/dev/null || echo "none")
  latest=$(echo "$json" | jq -r '.[0] | (.deploy // .) | "\((.commit.id // "?")[0:7]) \(.status // "?")"' 2>/dev/null || echo "?")

  echo "Render deploy of ${sha:0:7}: ${status}   (Render's newest deploy: ${latest})"
  # Render sometimes misses the push notification from GitHub. If it still has not started this commit
  # after a few minutes, ask Render to deploy exactly this commit ourselves (once).
  if [ "$status" = "none" ] && [ "$triggered" = "0" ] && [ $(( $(date +%s) - start )) -ge "$trigger_after" ]; then
    triggered=1
    tcode=$(curl -sS -o /dev/null -w '%{http_code}' -X POST \
      -H "Authorization: Bearer ${RENDER_API_KEY}" -H "Content-Type: application/json" -H "Accept: application/json" \
      -d "{\"commitId\":\"${sha}\"}" \
      "https://api.render.com/v1/services/${service}/deploys" 2>/dev/null || echo "000")
    echo "Render did not start this commit by itself; asked Render to deploy it (HTTP ${tcode})."
  fi

  case "$status" in
    live) exit 0 ;;
    build_failed|update_failed|canceled|deactivated) echo "Deploy ended with: $status"; exit 1 ;;
  esac
  sleep 20
done

echo "Timed out waiting for Render to deploy ${sha:0:7}"
exit 1
