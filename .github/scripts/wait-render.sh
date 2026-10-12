#!/usr/bin/env bash
# Usage: wait-render.sh <render_service_id> <commit_sha> [timeout_minutes]
# Waits until Render reports a LIVE deploy of exactly this commit.
# Needs env: RENDER_API_KEY
set -uo pipefail

service="$1"
sha="$2"
timeout_min="${3:-25}"
deadline=$(( $(date +%s) + timeout_min * 60 ))

while [ "$(date +%s)" -lt "$deadline" ]; do
  json=$(curl -sS -H "Authorization: Bearer ${RENDER_API_KEY}" -H "Accept: application/json" \
    "https://api.render.com/v1/services/${service}/deploys?limit=10" 2>/dev/null || echo "[]")

  status=$(echo "$json" | jq -r --arg sha "$sha" \
    '[.[] | (.deploy // .) | select(.commit.id == $sha)] | .[0].status // "none"' 2>/dev/null || echo "none")

  echo "Render deploy of ${sha:0:7}: ${status}"
  case "$status" in
    live) exit 0 ;;
    build_failed|update_failed|canceled|deactivated) echo "Deploy ended with: $status"; exit 1 ;;
  esac
  sleep 20
done

echo "Timed out waiting for Render to deploy ${sha:0:7}"
exit 1
