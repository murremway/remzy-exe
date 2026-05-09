// Broadcast (OBS Window Capture) view controller. Subscribes to the
// BroadcastChannel published by the control window and animates verse
// changes Pewbeam-style with the active theme.

import { applyTheme, THEMES } from "./themes.js";
import { BroadcastBus, loadSettings } from "./state.js";

const stage = document.getElementById("stage");
const refEl = document.getElementById("ref");
const verseEl = document.getElementById("verse");
const transEl = document.getElementById("translation");
const watermark = document.getElementById("watermark");
const fsPrompt = document.getElementById("fullscreenPrompt");

const bus = new BroadcastBus("broadcast");
const settings = loadSettings();
applyTheme(stage, settings.themeId in THEMES ? settings.themeId : "selah");
stage.style.opacity = String(settings.opacity ?? 1);

let current = { reference: "", text: "", translationName: "King James Version" };
let hidden = false;
let pendingFullscreen = false;

function paint(payload) {
  current = payload ?? current;
  refEl.textContent = current.reference || "";
  verseEl.textContent = current.text || "";
  transEl.textContent = current.translationName || "";

  for (const el of [refEl, verseEl, transEl]) {
    el.classList.remove("fade-enter");
    void el.offsetWidth; // restart animation
    el.classList.add("fade-enter");
  }
}

bus.subscribe((msg) => {
  switch (msg.type) {
    case "live":
      hidden = false;
      stage.classList.remove("hidden");
      paint(msg.payload);
      break;
    case "hide":
      hidden = true;
      stage.classList.add("hidden");
      break;
    case "show":
      hidden = false;
      stage.classList.remove("hidden");
      paint(current);
      break;
    case "theme":
      if (THEMES[msg.themeId]) applyTheme(stage, msg.themeId);
      break;
    case "opacity":
      stage.style.opacity = String(Math.max(0, Math.min(1, msg.value ?? 1)));
      break;
    case "watermark":
      watermark.style.display = msg.show === false ? "none" : "block";
      break;
    case "placement":
      handlePlacement(msg);
      break;
    case "ping":
      bus.publish({ type: "pong" });
      break;
  }
});

/* ---------- Placement & fullscreen ---------- */
function handlePlacement(msg) {
  const s = msg.screen;
  if (s) {
    try {
      window.moveTo(Math.round(s.availLeft), Math.round(s.availTop));
      window.resizeTo(Math.round(s.availWidth), Math.round(s.availHeight));
    } catch (err) {
      bus.publish({ type: "placement-result", error: `move/resize failed (${err?.message ?? err})` });
    }
  }
  if (msg.fullscreen) {
    // requestFullscreen() needs a user gesture inside this window.
    // Try once silently — Chrome will allow it briefly after window.open().
    requestFs().catch(() => {
      pendingFullscreen = true;
      showFsPrompt();
    });
  } else if (document.fullscreenElement) {
    document.exitFullscreen?.();
    pendingFullscreen = false;
    hideFsPrompt();
  }
}

async function requestFs() {
  if (document.fullscreenElement) return;
  await document.documentElement.requestFullscreen({ navigationUI: "hide" });
}

function showFsPrompt() { fsPrompt.hidden = false; }
function hideFsPrompt() { fsPrompt.hidden = true; }

fsPrompt.addEventListener("click", async () => {
  try {
    await requestFs();
    pendingFullscreen = false;
    hideFsPrompt();
  } catch (err) {
    bus.publish({ type: "placement-result", error: `fullscreen denied (${err?.message ?? err})` });
  }
});

document.addEventListener("fullscreenchange", () => {
  if (document.fullscreenElement) hideFsPrompt();
  else if (pendingFullscreen) showFsPrompt();
});

document.addEventListener("keydown", async (e) => {
  if (e.key === "f" || e.key === "F") {
    e.preventDefault();
    if (document.fullscreenElement) {
      await document.exitFullscreen();
    } else {
      try {
        await requestFs();
        pendingFullscreen = false;
        hideFsPrompt();
      } catch { /* ignore */ }
    }
  }
});

window.addEventListener("beforeunload", () => bus.publish({ type: "broadcast-closing" }));

// Announce ourselves so the control window knows we're live.
bus.publish({ type: "broadcast-ready" });
