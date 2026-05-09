#!/usr/bin/env bash
# Yinka.Mac/build.sh — assembles dist/Yinka.app from the current sources.
# Pure macOS toolchain (Bash + python3 + sips + iconutil); no Xcode project,
# no Node, no .NET. Pass an optional output directory as the first arg.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT_DIR="${1:-$REPO_ROOT/dist}"
APP="$OUT_DIR/Yinka.app"
WEB="$APP/Contents/Resources/web"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "build.sh only runs on macOS." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required (xcode-select --install)." >&2
  exit 1
fi

PY="$(command -v python3)"

echo "Yinka.Mac → building $APP"
rm -rf "$APP"
mkdir -p \
  "$APP/Contents/MacOS" \
  "$APP/Contents/Resources" \
  "$WEB/js" \
  "$WEB/Data"

# --- Web app ---------------------------------------------------------------
cp "$SCRIPT_DIR/index.html"    "$WEB/index.html"
cp "$SCRIPT_DIR/broadcast.html" "$WEB/broadcast.html"
cp "$SCRIPT_DIR/style.css"     "$WEB/style.css"
cp "$SCRIPT_DIR/broadcast.css" "$WEB/broadcast.css"
cp -R "$SCRIPT_DIR/js/." "$WEB/js/"

# --- KJV data -------------------------------------------------------------
cp "$REPO_ROOT/Data/en_kjv.json" "$WEB/Data/en_kjv.json"

# --- Server ---------------------------------------------------------------
cp "$SCRIPT_DIR/server/yinka_server.py" "$WEB/yinka_server.py"

# --- Launcher (Contents/MacOS/Yinka) --------------------------------------
cp "$SCRIPT_DIR/server/launcher.sh" "$APP/Contents/MacOS/Yinka"
chmod +x "$APP/Contents/MacOS/Yinka"

# --- Info.plist -----------------------------------------------------------
cp "$SCRIPT_DIR/server/Info.plist" "$APP/Contents/Info.plist"

# --- Icon (AppIcon.icns) --------------------------------------------------
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT
ICONSET="$TMP_DIR/AppIcon.iconset"
mkdir -p "$ICONSET"

if [ -f "$SCRIPT_DIR/server/AppIcon.icns" ]; then
  cp "$SCRIPT_DIR/server/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
else
  echo "  · rendering icon (1024×1024 PNG via stdlib)…"
  PNG="$TMP_DIR/AppIcon-1024.png"
  "$PY" "$SCRIPT_DIR/server/make_icon.py" "$PNG"

  if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
    declare -a SIZES=(16 32 32 64 128 256 256 512 512 1024)
    declare -a NAMES=(
      "icon_16x16.png"
      "icon_16x16@2x.png"
      "icon_32x32.png"
      "icon_32x32@2x.png"
      "icon_128x128.png"
      "icon_128x128@2x.png"
      "icon_256x256.png"
      "icon_256x256@2x.png"
      "icon_512x512.png"
      "icon_512x512@2x.png"
    )
    for i in "${!SIZES[@]}"; do
      sips -z "${SIZES[$i]}" "${SIZES[$i]}" "$PNG" --out "$ICONSET/${NAMES[$i]}" \
        >/dev/null 2>&1
    done
    iconutil -c icns -o "$APP/Contents/Resources/AppIcon.icns" "$ICONSET"
  else
    echo "  · sips/iconutil not found, copying raw PNG (Finder will use a generic icon)."
    cp "$PNG" "$APP/Contents/Resources/AppIcon.png"
  fi
fi

# --- Touch the bundle so Finder reloads the icon --------------------------
touch "$APP"

echo
echo "Built $APP"
echo "Launch with:"
echo "  open '$APP'"
echo "Install with:"
echo "  cp -R '$APP' /Applications/"
