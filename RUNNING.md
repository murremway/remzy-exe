# Running Yinka on macOS — full walkthrough

This guide walks you from a fresh checkout to projecting Bible verses on an external display during a sermon. Every step lists what you should *see* so you can confirm you're on track.

If you're on Windows, see the **Windows — WPF shell** section in [README.md](README.md) — this guide is macOS-only.

---

## 1. Prerequisites

You need these once. After this, Yinka has no other dependencies.

| Requirement | Why | How to check / install |
|-------------|-----|------------------------|
| **macOS 11 (Big Sur) or newer** | App bundle minimum target. | Apple menu → About This Mac. |
| **`/usr/bin/python3`** | Bundled HTTP server runtime. macOS Application Firewall trusts the system Python; Homebrew Python is silently blocked. | `python3 --version` should print `Python 3.x.x`. If it errors, run `xcode-select --install` and click **Install** in the popup. |
| **Google Chrome** (or Microsoft Edge / Brave) | Live mic transcription needs `webkitSpeechRecognition`, which Safari does *not* expose. The dashboard still works in Safari, but the **Start Transcribing** button will refuse to start. | Download from [google.com/chrome](https://www.google.com/chrome). |
| **A microphone** *(optional)* | Only needed if you want auto-detect-while-the-pastor-speaks. Manual / paste-in workflow doesn't need a mic. | System Settings → Sound → Input. |
| **An external display, projector, or OBS** *(optional)* | Where the verses are presented. The control window stays on your laptop; the broadcast window goes here. | HDMI / USB-C / AirPlay all work. |

**Don't have Chrome?** You can still use Yinka — paste a transcript into the **Live Transcript** panel instead of using the mic. Skip the "Live transcription" step in this guide.

---

## 2. One-time setup — build the `.app`

The `.app` bundle is self-contained: a single drag-to-Applications artifact with the KJV, the dashboard, and the local server all baked in.

1. Open **Terminal** (`⌘+Space` → type "terminal" → Enter).
2. Move into the repo:
   ```bash
   cd /path/to/yinka
   ```
   Replace `/path/to/yinka` with the actual location, e.g.
   `cd ~/Desktop/yinka`.
3. Build the bundle:
   ```bash
   ./Yinka.Mac/build.sh
   ```

   **What you'll see:**
   ```
   Yinka.Mac → building /path/to/yinka/dist/Yinka.app
     · rendering icon (1024×1024 PNG via stdlib)…

   Built /path/to/yinka/dist/Yinka.app
   Launch with:
     open '/path/to/yinka/dist/Yinka.app'
   ```

   The build takes ~3 seconds and produces a ~4.8 MB bundle.
4. *(Optional but recommended)* install it like any other Mac app:
   ```bash
   cp -R dist/Yinka.app /Applications/
   ```

   Now **Yinka** shows up in **Launchpad**, **Spotlight** (`⌘+Space` → "yinka"), and you can pin it to the Dock.

> **You only need to rebuild when you change the source.** The bundled assets live entirely inside `Yinka.app/Contents/Resources/web/` — once built, the .app works on any Mac that has `/usr/bin/python3`, no internet required.

---

## 3. Launch Yinka

### From Finder, Spotlight, or Dock

Double-click **Yinka** (or single-click the Dock icon if you pinned it).

**What you'll see:**

1. The Yinka icon bounces in the Dock and stays.
2. A new Chrome window opens at `http://127.0.0.1:8731/index.html`.
3. The dashboard loads — six panels on a dark background. Top-left a small toast says **"KJV ready (offline). Press Start Transcribing or paste a transcript."**

### From Terminal (equivalent)

```bash
open dist/Yinka.app
# or, if you installed it:
open -a Yinka
```

### What's happening behind the scenes

```
Dock icon (Yinka)
   └─ /bin/bash  …/Contents/MacOS/Yinka
        ├─ checks 127.0.0.1:8731/__alive  (single-instance guard)
        ├─ picks first free port from [8731,8741,8751,8761,8781,8791,8821,8831]
        ├─ starts /usr/bin/python3 yinka_server.py <port> <web-root>
        ├─ waits for /__alive to respond
        └─ opens the dashboard in Chrome (or Edge / Brave / default)
```

If you double-click Yinka while it's already running, no new server is started — the existing one just opens a fresh browser tab on the same port.

---

## 4. The dashboard at a glance

```
┌──────────────────────────────────────────────────────────────────────┐
│ Yinka  [Manual|Auto]  Theme▾  Conf=60%  Opacity=100%  Display▾  Fullscreen ☐  [Open Broadcast]  [Quit Yinka] │
├────────────────┬──────────┬───────────┬───────────────────────────────┤
│ Live Transcript│ Preview  │ Live Output│ Queue                        │
│  · audio meter │  ↓ Go    │  ON AIR ●  │  · play / × per row          │
│  · highlights  │     Live │  · current │  · Clear All                 │
├────────────────┴──────────┼───────────┴───────────────────────────────┤
│ Scripture Search          │ Detections                                │
│  Book ⇆ Context (Tab)     │  · auto-scanned from transcript          │
│  · type "John 3:16" or    │  · confidence + Direct/Semantic badge     │
│    a phrase → results     │  · ▶ display  · + queue                   │
└───────────────────────────┴───────────────────────────────────────────┘
```

Skim the table once and you're done — the rest of this guide is just "where to click for X".

| Panel | Purpose |
|-------|---------|
| **Live Transcript** | Real-time captions or a typed/pasted transcript. Detected references are highlighted gold inline. The horizontal bar in the header is your audio level meter. |
| **Preview** | Staging area. Loading a verse here doesn't affect the broadcast unless **Go Live** is on. |
| **Live Output** | Mirror of what's actually being broadcast. The pulsing red **ON AIR** badge tells you the broadcast is hot. |
| **Queue** | Verses you've pre-loaded. Click **▶** to send one live, **×** to remove, **Clear All** to empty. |
| **Scripture Search** | Manual lookup. **Book** mode for references; **Context** mode for phrases / topics. Press **Tab** to switch. |
| **Detections** | Auto-detected references from the transcript with a confidence score and source badge. |

---

## 5. Three workflows

Pick whichever matches your service. All three coexist — you can mix them mid-sermon.

### A. Manual presenter (no mic)

The simplest. Use this if you want full control or your mic feed is unreliable.

1. Click the **Scripture Search** input.
2. Type a reference, e.g. `John 3:16`. Press **Enter** — the verse appears in **Preview**.
3. Click **Go Live (L)** in the Preview panel (or press **L** with no input focused). It moves to **Live Output** and the **ON AIR** badge turns red.
4. Repeat for the next verse.

To pre-load a whole sermon outline:

1. In **Scripture Search**, look up each verse and click the **+** icon — it goes to the **Queue** instead of Preview.
2. During the service, click **▶** next to the queue item to send it live.

### B. Live-listening / auto-detect (Pewbeam-style)

Yinka listens to the mic, transcribes in real time, scans the transcript for Bible references, and (in **Auto** mode) pushes detected verses to broadcast.

1. **Grant mic permission.** First time you click **Start Transcribing**, Chrome asks "127.0.0.1:8731 wants to use your microphone". Click **Allow**.
2. Click **Start Transcribing** in the **Live Transcript** panel.
   - The button turns red and reads **Stop Transcribing**.
   - The audio meter in the header bobs green/yellow as you speak.
   - A blinking cursor follows the latest words.
3. Speak (or play a recording with audio routed to your input device). When you say a reference like *"please turn to John chapter three verse sixteen,"* it lights up gold in the transcript and appears in the **Detections** panel within ~1 second.
4. **Manual mode** (default): click **▶** next to the detection to send it to Preview, then **L** or click **Go Live**.
5. **Auto mode**: click **Auto** in the top bar. Now any detection above the **Confidence ≥** threshold (default 60%, direct refs are ~95%) is automatically pushed live, with a 2.5-second cooldown to prevent flicker.

> **Audio quality matters.** A direct USB feed from your church mixing board produces wildly better transcription than a laptop mic picking up room sound. Set the Input Device in **System Settings → Sound → Input** before starting.

### C. Topic / paraphrase search (Context mode)

For when you don't know the reference but know the topic.

1. Click the **Scripture Search** segment **Context** (or focus the input and press **Tab**). The label changes to "Context mode".
2. Type a phrase: `the Lord is my shepherd`, `faith without works`, `love your neighbor`, `God so loved the world`.
3. Press **Enter**. You get up to 12 ranked results scored by keyword overlap.
4. Click **▶** to display, or **+** to queue.

> Context mode is offline keyword search across all 31,000+ KJV verses. It's not AI embeddings (that needs a server-side model), but for keywords / paraphrases of recognizable phrases it works very well.

---

## 6. Setting up the projector (extended display + fullscreen)

Yinka uses Chrome's [Window Management API](https://developer.mozilla.org/docs/Web/API/Window_Management_API) so you can place the broadcast window directly on your projector / external monitor and run it fullscreen.

1. Plug in the projector / external monitor.
2. In Yinka's top bar, click the small **refresh** link next to **Display**.
   - Chrome shows a popup: **"127.0.0.1:8731 wants to manage windows on all your displays"**. Click **Allow**.
   - The **Display** dropdown populates. Your laptop screen is marked with **★** (primary). The projector appears as something like `External display 2 · 1920×1080`.
3. Pick the projector from the dropdown. (Yinka auto-pre-selects the first non-primary display, so this is usually already done for you.)
4. Tick the **Fullscreen** checkbox.
5. Click **Open Broadcast**.
   - A new Chrome popup window appears. It's auto-resized to fill the projector.
   - On the popup, you'll see a dimmed **"Click to enter fullscreen"** card. **Click anywhere on it.** (Browsers require a user gesture *inside* the popup to enter fullscreen — they won't honor it from the dashboard for security reasons.)
   - Boom — fullscreen verses on the projector. Press **F** anytime to toggle, **Esc** to exit.
6. Send a verse via any of the workflows above. The projector smoothly cross-fades to the new verse.

To change which display Yinka uses mid-service, just pick a different option in the dropdown. The popup is moved automatically. Your selection persists between sessions in `localStorage`.

### Themes

Three built-in themes (top bar → **Theme** dropdown) — switching is instant on the broadcast window:

| Theme | Look | When to use |
|-------|------|-------------|
| **Selah** | Warm dark background, gold accents, Cormorant Garamond serif. | Default. Looks dignified on most projectors. |
| **Eden** | Green/teal gradient, Inter sans-serif. | High-contrast on darker rooms, modern look. |
| **OBS Chroma Key** | Solid `#00FF00` background. | For OBS Color Key — verses become a transparent overlay. |

---

## 7. OBS / streaming setup

If you stream services to YouTube / Facebook / vMix etc., you can capture Yinka's broadcast window with OBS.

1. Open Yinka and click **Open Broadcast**. You can leave the popup *not* fullscreen for OBS — the window title is what OBS keys on, and a smaller window keeps the rest of your desktop usable.
2. In **OBS Studio**, in your scene, click **+** under **Sources** → **Window Capture**.
3. In the dropdown, pick the window titled exactly **`Yinka — OBS Window Capture (KJV verse)`**.
4. *(Optional, for transparent overlays)* Back in Yinka, set **Theme** to **OBS Chroma Key**. The broadcast window turns pure green. In OBS, on that source, click **Filters** → **+** → **Color Key** → set Key Color Type to **Green**. Yinka now overlays your livestream as a clean transparent caption.

---

## 8. Keyboard shortcuts

These work when no text input is focused. Press **Esc** first if you've been typing.

| Key | Where | Action |
|-----|-------|--------|
| **L** | Control window | Toggle Go Live. If a Preview is loaded, also pushes it live. |
| **Tab** | Control window | Switch Scripture Search between Book and Context mode. |
| **Enter** | Search input | Preview the top match. |
| **Enter** twice (within ~380 ms) | Search input | Preview + Go Live in one motion. |
| **0**–**9** | Search results showing a chapter | Jump to that verse. |
| **Esc** | Anywhere | Blur the focused input so the global shortcuts work again. |
| **F** | Broadcast window | Toggle fullscreen. |
| **Esc** | Broadcast window | Exit fullscreen. |

---

## 9. Quitting Yinka

There are three ways to stop the app. Use the first one.

### ✅ Click "Quit Yinka" (recommended)

Top-right of the dashboard. Confirms, then:

- Closes the broadcast popup.
- Stops the mic and frees the audio device.
- Hits `/__quit` on the server, which exits cleanly.
- The Dock icon disappears.
- The browser tab shows **"Yinka stopped. You can close this tab."**

### ⚠️ Closing the browser tab

The browser tab goes away, but **the server keeps running** and the Dock icon stays. To stop it, either:

- Re-open the dashboard at `http://127.0.0.1:8731/index.html` and click **Quit Yinka**, or
- Force-quit from the Dock (right-click Yinka → **Force Quit**), or
- Run `lsof -nP -iTCP:8731 -sTCP:LISTEN -t | xargs kill` in Terminal.

### 🛑 Cmd-Q from the Dock

Because Yinka.app is a shell-script wrapper (not a Cocoa app), `Cmd-Q` from the Dock is equivalent to a force-kill — the Python server doesn't get a chance to shut down gracefully. It works, but the **Quit Yinka** button is preferred.

---

## 10. Dev mode (no `.app` build)

For development or one-off use, you can skip the build entirely:

```bash
cd /path/to/yinka
./Yinka.Mac/run.sh
```

**What you'll see:**

```
Yinka.Mac → serving /path/to/yinka on http://127.0.0.1:8731
  Control window: http://127.0.0.1:8731/Yinka.Mac/index.html
  Stop the server with Ctrl-C.
```

Chrome opens automatically. Edit any file in `Yinka.Mac/` and reload the browser tab — no rebuild step. Stop with **Ctrl-C** in the terminal.

Override the port if `8731` is busy:

```bash
YINKA_PORT=9000 ./Yinka.Mac/run.sh
```

---

## 11. Troubleshooting

Reach for these in order if something breaks.

### "site can't be reached" / browser shows ERR_CONNECTION_REFUSED

The Python server didn't start, or the macOS Application Firewall is blocking it.

1. Check the firewall:
   ```bash
   /usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate
   ```
   If it says **enabled** + **stealth mode on**, Apple's `/usr/bin/python3` is allowlisted but Homebrew Python isn't. The launcher already prefers the system Python — make sure you have it: `ls -l /usr/bin/python3`.
2. Make sure no zombie process is holding the port:
   ```bash
   lsof -nP -iTCP:8731
   # if anything shows up → kill it:
   lsof -nP -iTCP:8731 -t | xargs kill -9
   ```
3. Re-launch the app.

### "Quit Yinka" did nothing / Dock icon won't go away

Force-quit from the Dock (right-click → Force Quit), or:

```bash
pkill -f "Yinka.app/Contents/MacOS/Yinka"
pkill -f "yinka_server.py"
```

### Start Transcribing button does nothing

You're in Safari. Switch to Chrome / Edge / Brave. Safari hasn't shipped `webkitSpeechRecognition`.

If you're already in Chrome:

1. Click the lock icon left of the URL → **Site settings** → **Microphone** → **Allow**.
2. Reload the page.
3. Make sure your **System Settings → Privacy & Security → Microphone** has Chrome enabled.

### Broadcast popup blocked

The first time you click **Open Broadcast**, Chrome may block the popup. Click the address-bar **popup blocked** indicator → **Always allow popups from 127.0.0.1** → click Open Broadcast again.

### Display dropdown only shows "This window (centered popup)" after granting permission

Only one display is connected, or Chrome doesn't see your projector. macOS sometimes doesn't expose mirrored displays as separate. Open **System Settings → Displays** and confirm **Use as: Extended display** (not Mirror).

### Auto mode never triggers

Lower the **Confidence ≥** threshold in the top bar. Direct references like "John 3:16" detect at ~95%, so the default 60% should always trigger — if they're not, you probably never spoke a clean reference. Check the **Detections** panel: nothing there means the transcript isn't picking up the reference (use the **Insert Sample** button in Live Transcript to verify auto-mode plumbing without a mic).

### KJV not loaded

If the toast says "KJV failed to load":

- For the `.app`: rebuild with `./Yinka.Mac/build.sh` — the bundled `Data/en_kjv.json` may be missing.
- For dev mode: confirm `Data/en_kjv.json` exists at the repo root (4.5 MB) and that you launched `run.sh` from the repo root.

### Port 8731 conflicts with something else

Set a different port:

- For `.app`: the launcher already auto-falls-back to `8741, 8751, 8761, 8781, 8791, 8821, 8831`. If all of those are taken (very unusual), edit `Yinka.Mac/server/launcher.sh` and add more.
- For dev: `YINKA_PORT=9000 ./Yinka.Mac/run.sh`.

### I changed source files but the .app still shows the old version

Rebuild:

```bash
./Yinka.Mac/build.sh
```

The `.app` bundles a snapshot of the web assets — it doesn't hot-reload from `Yinka.Mac/`.

---

## 12. Quick reference card

Print this and tape it to your projector booth:

```
LAUNCH        Double-click Yinka in Dock / Launchpad
QUIT          Click "Quit Yinka" top-right of dashboard
GO LIVE       Press L  (or click Go Live in Preview)
SEARCH        Click search box, type "John 3:16", press Enter
SEARCH MODE   Press Tab  (Book ↔ Context)
JUMP VERSE    Press 0–9 when viewing a chapter
PROJECTOR     Display dropdown → pick screen → tick Fullscreen → Open Broadcast
              (then click "Click to enter fullscreen" overlay once)
FULLSCREEN    Press F in the broadcast window
EXIT FS       Press Esc in the broadcast window
ON AIR        Pulsing red badge in Live Output panel
SHORTCUTS     Press Esc first if shortcuts aren't working (you have an input focused)
```

---

Now go preach.
