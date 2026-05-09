#!/bin/bash
# Yinka.app launcher — Contents/MacOS/Yinka
# Spawns the bundled Python HTTP server, opens the dashboard in a browser,
# then waits until the user clicks "Quit Yinka" (which hits /__quit).

set -u

BUNDLE="$(cd "$(dirname "$0")/.." && pwd)"
WEB="$BUNDLE/Resources/web"
SERVER_PY="$WEB/yinka_server.py"

alert() {
  /usr/bin/osascript -e "display alert \"Yinka\" message \"$1\"" >/dev/null 2>&1 || true
}

# --- Locate python3 -----------------------------------------------------------
# Prefer Apple's /usr/bin/python3: macOS Application Firewall (stealth mode)
# trusts it by default. Homebrew/MacPorts python binaries are blocked unless
# the user runs `socketfilterfw --add`, which would silently break loopback
# binds. Fall back to other interpreters if /usr/bin/python3 is missing.
PY=""
for candidate in /usr/bin/python3 /opt/homebrew/bin/python3 /usr/local/bin/python3; do
  if [ -x "$candidate" ]; then PY="$candidate"; break; fi
done
if [ -z "$PY" ]; then
  PY="$(command -v python3 2>/dev/null || true)"
fi
if [ -z "$PY" ]; then
  alert "python3 wasn't found. Install the Xcode Command Line Tools: xcode-select --install"
  exit 1
fi

# --- Single-instance: re-use a running Yinka if its /__alive responds --------
PREFERRED_PORTS=(8731 8741 8751 8761 8781 8791 8821 8831)
for p in "${PREFERRED_PORTS[@]}"; do
  if /usr/bin/curl -sf -m 0.4 "http://127.0.0.1:${p}/__alive" >/dev/null 2>&1; then
    /usr/bin/open "http://127.0.0.1:${p}/index.html"
    exit 0
  fi
done

# --- Pick a free port --------------------------------------------------------
PORT=""
for p in "${PREFERRED_PORTS[@]}"; do
  if ! /usr/sbin/lsof -nP -iTCP:"$p" -sTCP:LISTEN >/dev/null 2>&1; then
    PORT="$p"; break
  fi
done
if [ -z "$PORT" ]; then
  alert "Could not find a free local port (tried ${PREFERRED_PORTS[*]})."
  exit 1
fi

URL="http://127.0.0.1:${PORT}/index.html"

# --- Start server in background and trap on exit -----------------------------
"$PY" "$SERVER_PY" "$PORT" "$WEB" >/dev/null 2>&1 &
SERVER_PID=$!

cleanup() {
  if kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

# --- Wait for /__alive then open the browser --------------------------------
for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
  if /usr/bin/curl -sf -m 0.4 "http://127.0.0.1:${PORT}/__alive" >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    alert "Yinka server failed to start on port ${PORT}."
    exit 1
  fi
  sleep 0.2
done

if [ -d "/Applications/Google Chrome.app" ]; then
  /usr/bin/open -na "Google Chrome" --args --new-window "$URL"
elif [ -d "/Applications/Microsoft Edge.app" ]; then
  /usr/bin/open -na "Microsoft Edge" --args --new-window "$URL"
elif [ -d "/Applications/Brave Browser.app" ]; then
  /usr/bin/open -na "Brave Browser" --args --new-window "$URL"
else
  /usr/bin/open "$URL"
fi

# --- Hold the .app process open until the server exits (via /__quit) --------
wait "$SERVER_PID"
