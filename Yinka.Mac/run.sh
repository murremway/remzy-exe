#!/usr/bin/env bash
# Yinka.Mac launcher — serves the static app from the repo root so
# JS can fetch ../Data/en_kjv.json, then opens Yinka in the browser.
# Requires python3 (ships with macOS via Xcode CLT or Homebrew).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PORT="${YINKA_PORT:-8731}"
URL="http://127.0.0.1:${PORT}/Yinka.Mac/index.html"

# Prefer Apple's /usr/bin/python3: macOS Application Firewall (stealth mode)
# blocks Homebrew Python from accepting incoming sockets, even on loopback.
PY=""
for candidate in /usr/bin/python3 /opt/homebrew/bin/python3 /usr/local/bin/python3; do
  if [ -x "$candidate" ]; then PY="$candidate"; break; fi
done
if [ -z "$PY" ]; then PY="$(command -v python3 2>/dev/null || true)"; fi
if [ -z "$PY" ]; then
  echo "python3 was not found in PATH. Install Xcode Command Line Tools:" >&2
  echo "  xcode-select --install" >&2
  exit 1
fi

if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port $PORT is in use. Set YINKA_PORT=<port> and try again." >&2
  exit 1
fi

echo "Yinka.Mac → serving $REPO_ROOT on http://127.0.0.1:${PORT}"
echo "  Control window: $URL"
echo "  Stop the server with Ctrl-C."

# Prefer Chrome (has webkitSpeechRecognition for live captions).
open_browser() {
  if [ -d "/Applications/Google Chrome.app" ]; then
    open -na "Google Chrome" --args --new-window "$URL" || open "$URL"
  elif [ -d "/Applications/Microsoft Edge.app" ]; then
    open -na "Microsoft Edge" --args --new-window "$URL" || open "$URL"
  else
    open "$URL"
  fi
}

(sleep 0.6 && open_browser) &

cd "$REPO_ROOT"
exec "$PY" -m http.server "$PORT" --bind 127.0.0.1
