#!/usr/bin/env bash
# Usage: notify.sh "message" [issue_number]
# With an issue number, the message gets the [Good] [Changes] [Cancel] buttons.
# Needs env: TELEGRAM_BOT_TOKEN, CHAT_ID
set -uo pipefail

text="$1"
issue="${2:-}"

if [ -n "$issue" ]; then
  payload=$(jq -n --arg chat "$CHAT_ID" --arg text "$text" --arg n "$issue" '{
    chat_id: $chat,
    text: $text,
    reply_markup: { inline_keyboard: [
      [ {text: "👍 Good, publish it", callback_data: ("approve:" + $n)},
        {text: "👎 Needs changes",    callback_data: ("changes:" + $n)} ],
      [ {text: "Cancel this request", callback_data: ("cancel:" + $n)} ]
    ] }
  }')
else
  payload=$(jq -n --arg chat "$CHAT_ID" --arg text "$text" '{chat_id: $chat, text: $text}')
fi

curl -sS -X POST "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/sendMessage" \
  -H 'Content-Type: application/json' -d "$payload" >/dev/null \
  || echo "::warning::Telegram notification failed"
exit 0
