# Yinka — KJV verse presenter

Desktop verse-presentation app for **offline King James Version** text, **reference detection** in a transcript, **preview / queue / go-live**, and an **OBS-friendly broadcast window** (fixed window title for **Window Capture**).

There are two UI shells. They share the same KJV reference parser and offline KJV store under different roofs:

| Project | Platform | UI stack | Notes |
|--------|----------|----------|--------|
| **`Yinka`** | **Windows 10+** | WPF (.NET 8) | **Windows Speech** live captions, native screen enumeration. |
| **`Yinka.Mac`** | **macOS** (Chrome/Edge) | HTML/CSS/JS, Pewbeam-style dashboard | Live mic transcription via Web Speech API, animated themed broadcast window for OBS. **No .NET required.** |

Bundled scripture: **`Data/en_kjv.json`** (from [thiagobodruk/bible `en_kjv.json`](https://raw.githubusercontent.com/thiagobodruk/bible/master/json/en_kjv.json), public JSON). It's loaded at runtime by both shells.

---

## macOS — Pewbeam-style web app (`Yinka.Mac`)

The macOS shell was rewritten as a static web app that runs in your browser. The only requirement is **`python3`** (already installed on macOS via the Xcode Command Line Tools). No .NET, no Node, no installer.

> **Looking for a step-by-step walkthrough?** See **[RUNNING.md](RUNNING.md)** — it covers prerequisites, building the `.app`, every workflow (manual / auto / context search), projector setup, OBS, and troubleshooting in one place.

You can run it two ways:

1. **As a real `.app`** — build a self-contained `Yinka.app` bundle (clickable in Finder, draggable to `/Applications`). See [Build the macOS `.app`](#build-the-macos-app).
2. **In dev mode** — run the launcher script and open the dashboard in Chrome.

### Run in dev mode

```bash
cd /path/to/yinka
./Yinka.Mac/run.sh
```

The launcher serves the repo on `http://127.0.0.1:8731` and opens **`Yinka.Mac/index.html`** in Chrome (or Edge if Chrome isn't installed). Override the port with `YINKA_PORT=9000 ./Yinka.Mac/run.sh`. Stop the server with `Ctrl-C`.

### Build the macOS `.app`

```bash
./Yinka.Mac/build.sh                # → dist/Yinka.app
./Yinka.Mac/build.sh /tmp/build     # custom output directory
```

The build script (~3 seconds) assembles a fully self-contained bundle:

```
dist/Yinka.app/
  Contents/
    Info.plist
    MacOS/Yinka                     ← shell launcher (Apple system python3)
    Resources/
      AppIcon.icns                  ← rendered from server/make_icon.py
      web/
        index.html
        broadcast.html
        style.css · broadcast.css
        js/                         ← all dashboard modules
        Data/en_kjv.json            ← bundled KJV (4.5 MB)
        yinka_server.py             ← static-file server + /__alive + /__quit
```

Total bundle size is ~4.8 MB. Drag it into `/Applications` (or anywhere you like) and double-click to launch.

### What happens at launch

1. The Dock icon appears.
2. The launcher (`Contents/MacOS/Yinka`) checks `127.0.0.1:8731/__alive` — if Yinka is already running, it just reopens the browser tab and exits (single-instance).
3. Otherwise it picks a free port from `8731, 8741, 8751, 8761, 8781, 8791, 8821, 8831`, starts the bundled Python server, waits for `/__alive`, then opens the dashboard in Chrome (falls back to Edge → Brave → default).
4. The launcher process stays alive (`wait`-ing on the Python child), which keeps the Dock icon visible.
5. Click **Quit Yinka** in the dashboard top bar to stop the server. The Python process exits cleanly via `/__quit`, the launcher exits, and the Dock icon disappears.

> **Why the system Python?** macOS Application Firewall (stealth mode) silently blocks Homebrew/MacPorts Python from accepting incoming sockets — even on `127.0.0.1`. The launcher prefers `/usr/bin/python3` (which the firewall trusts) and only falls back to `/opt/homebrew/bin/python3` or `/usr/local/bin/python3` if the system Python isn't available. The first time you ever ran Xcode CLT or `xcode-select --install`, you got the system Python at `/usr/bin/python3`.

### Custom icon

`build.sh` renders the default Pewbeam-ish gold-Y-on-dark icon via `Yinka.Mac/server/make_icon.py` (pure stdlib PNG generator) and converts it to an `.icns` using `sips` + `iconutil`. To use your own icon, drop a 1024×1024 `AppIcon.icns` at `Yinka.Mac/server/AppIcon.icns` — the build script will use it instead of regenerating.

> **Browser tip:** live mic transcription uses the Web Speech API, which only works in Chromium-based browsers (Chrome / Edge / Brave) on macOS. Safari can still load the app, but you'll need to paste your transcript instead of speaking it.

### Dashboard layout

Modeled after Pewbeam's six-panel dashboard:

| Panel | Where | What it does |
|-------|-------|-------------|
| **Live Transcript** | top-left | Real-time captions from the mic (Web Speech API); detected refs are highlighted inline; audio level meter in the header. You can also paste / type a transcript directly. |
| **Preview** | top-center-left | Staging area — verse shown here goes to **Live** when **Go Live** is on (or when you press **L**). |
| **Live Output** | top-center-right | What's being broadcast right now. **ON AIR** badge pulses red when active. |
| **Queue** | top-right | Playlist of verses to present. Play / remove per row, or **Clear All**. |
| **Scripture Search** | bottom-left | **Book mode** for direct lookup ("John 3:16", "Ps 23"); **Context mode** for keyword/phrase search across the whole KJV. **Tab** toggles. **Enter** previews; **Enter twice** previews + goes live. |
| **Detections** | bottom-right | Auto-detected references from the transcript with confidence and source badge. |

### Display modes

- **Manual** (default) — verses go to broadcast only when you press play / Go Live.
- **Auto** — the highest-confidence detection above the **confidence threshold** is automatically pushed live, with a 2.5-second cooldown to avoid flicker.

### Themes

Two built-in themes for the broadcast window:

- **Selah** — dark surface with gold accents (`Cormorant Garamond` for verse text)
- **Eden** — green/teal gradient with light foreground (Inter)
- **OBS Chroma Key** — solid `#00FF00` background for OBS Color Key compositing

The active theme is applied to the popup broadcast window in real time via `BroadcastChannel`. Change the theme any time from the top bar.

### Multi-display / extended screen

Yinka uses the [Window Management API](https://developer.mozilla.org/docs/Web/API/Window_Management_API) so you can throw the broadcast to a projector or external monitor:

1. In the top bar, click the **refresh** link next to **Display**. Chrome will prompt for *Window Management* permission — click **Allow**.
2. The dropdown populates with every connected screen (★ marks your primary). Pick the one your projector/HDMI display is on.
3. Tick **Fullscreen** if you want it borderless on that screen.
4. Click **Open Broadcast**. The popup is moved & sized to fill the chosen display.
5. If fullscreen was requested, the popup shows a one-time **Click to enter fullscreen** overlay (browsers require a user gesture inside the popup itself). Click it and you're done. Press `F` any time to toggle, `Esc` to exit.

The selected display + fullscreen preference persist between sessions. If you only see "This window (centered popup)" after granting permission, only one display is connected.

### OBS setup

1. Click **Open Broadcast** in the top bar — a popup window opens titled exactly **"Yinka — OBS Window Capture (KJV verse)"**.
2. In OBS, add **Window Capture** and pick that title.
3. Optional: set **Theme = OBS Chroma Key** in Yinka and add a **Color Key** filter in OBS for transparent overlays.
4. If you'd rather project directly without OBS, use the **Display** picker above and run the broadcast fullscreen on the projector.

### Keyboard shortcuts

| Key | Action | Where |
|-----|--------|-------|
| `L` | Toggle Go Live (when no text field is focused). If preview is loaded, also pushes it live. | control |
| `Tab` | Switch Search between Book mode and Context mode. | control |
| `Enter` | (Search) preview the top match. | control |
| `Enter` twice (within ~380 ms) | Preview + go live. | control |
| `0`–`9` | When viewing a chapter, jump to that verse. | control |
| `Esc` | Blur the focused input so global shortcuts work again. | control |
| `F` | Toggle fullscreen. | broadcast |
| `Esc` | Exit fullscreen. | broadcast |

### File layout

```
Yinka.Mac/
  index.html            ← control dashboard
  broadcast.html        ← OBS broadcast window (themed)
  style.css             ← dashboard styles (dark, Pewbeam-style grid)
  broadcast.css         ← broadcast window styles + animated transitions
  run.sh                ← dev launcher (python3 -m http.server + Chrome)
  build.sh              ← assembles dist/Yinka.app
  js/
    parser.js           ← Bible reference parser (port of Yinka.Core)
    store.js            ← Offline KJV store (port of Yinka.Core)
    search.js           ← Book + Context search
    transcript.js       ← Web Speech API + audio level meter
    themes.js           ← Selah, Eden, OBS Chroma Key
    state.js            ← BroadcastChannel + persisted settings
    app.js              ← control window controller
    broadcast.js        ← broadcast window controller
  server/
    yinka_server.py     ← static-file server + /__alive + /__quit endpoints
    launcher.sh         ← gets copied to .app/Contents/MacOS/Yinka
    Info.plist          ← gets copied to .app/Contents/Info.plist
    make_icon.py        ← stdlib-only 1024×1024 PNG renderer for the icon
```

`store.js` probes `Data/en_kjv.json` first (.app bundle layout), then falls back to `../Data/en_kjv.json` (dev layout where the server runs from the repo root). The same JS works in both modes without changes.

---

## Windows — WPF shell (`Yinka`)

The Windows shell is a native WPF app that can be published as a self-contained
`Yinka.exe` folder (no .NET runtime install required for end users).

> **Detailed Windows walkthrough:** see **[WINDOWS.md](WINDOWS.md)** for prerequisites, one-command publishing, speech setup, projector setup, OBS setup, troubleshooting, and distribution notes.

Build a distributable Windows x64 release from a Windows machine:

```powershell
.\build-windows.ps1
```

Output:

```text
dist\windows\Yinka-win-x64\
  Yinka.exe
  Data\en_kjv.json
  ... self-contained .NET runtime files ...

dist\windows\Yinka-win-x64.zip
```

Run locally:

```powershell
.\dist\windows\Yinka-win-x64\Yinka.exe
```

For development:

```powershell
cd path\to\yinka
dotnet restore
dotnet build .\Yinka.sln -c Release
dotnet run --project .\Yinka\Yinka.csproj -c Release
```

Or open **`Yinka.sln`** in Visual Studio and start the **`Yinka`** project. The .NET-based `Yinka.Mac` project has been removed from the solution; only `Yinka.Core` and the Windows `Yinka` project remain there.

**Windows speech**: allow the **microphone** in **Settings → Privacy & security → Microphone**. For **offline** recognition, install the same **language** with **offline speech** assets under **Settings → Time & language**.

---

## Repository layout

```
yinka/
  README.md
  Data/
    en_kjv.json           ← full KJV (loaded by both shells)
  Yinka.Core/             ← shared C# parser + KJV store (used by Windows shell)
  Yinka/                  ← Windows WPF app (.NET 8)
  Yinka.Mac/              ← macOS web app (HTML/CSS/JS, no .NET)
    server/               ← .app launcher, Info.plist, custom server, icon renderer
    build.sh              ← assembles dist/Yinka.app
    run.sh                ← dev launcher
  Yinka.sln               ← Yinka.Core + Windows Yinka
  dist/Yinka.app          ← built output (gitignored)
```

---

## Troubleshooting

| Issue | What to try |
|-------|----------------|
| `python3` not found on Mac | `xcode-select --install` — installs `/usr/bin/python3`. |
| Yinka.app launches but the browser shows "site can't be reached" | macOS Application Firewall is blocking your `python3`. The launcher prefers Apple's `/usr/bin/python3` (which is allowlisted). If you only have Homebrew Python, allow it: System Settings → Network → Firewall → Options → add `python3` → Allow incoming connections. |
| Yinka.app icon stays in Dock after closing the browser tab | Closing the tab doesn't stop the server. Click **Quit Yinka** in the dashboard top bar — that hits `/__quit` and the launcher exits cleanly. |
| Mic transcription button does nothing | Use Chrome or Edge — Safari doesn't expose `webkitSpeechRecognition`. Then grant the site mic permission. |
| Broadcast popup blocked | Allow popups for `127.0.0.1:8731`, then click **Open Broadcast** again. |
| KJV not loaded | If you're running the `.app`, rebuild with `./Yinka.Mac/build.sh`. In dev mode, launch from the repo root with `./Yinka.Mac/run.sh`. |
| OBS doesn't see the window | Confirm the popup is visible and titled **`Yinka — OBS Window Capture (KJV verse)`**; refresh OBS sources. |
| Auto mode never fires | Lower the confidence threshold in the top bar; direct refs detect at ~95% so the default 60% should always trigger. |

---

## License / text

KJV text bundled as JSON follows the dataset's license (public domain text; see the upstream **thiagobodruk/bible** repository). Application code is provided as-is for your own use and modification.
