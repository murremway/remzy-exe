// Yinka — Pewbeam-style control window. Wires the dashboard panels,
// mic transcription, reference detection, search, queue, themes, and the
// broadcast (OBS) window via BroadcastChannel.

import { findReferences } from "./parser.js";
import { kjvStore } from "./store.js";
import { resolveBookQuery, contextSearch } from "./search.js";
import {
  createTranscriber,
  isSpeechSupported,
  renderTranscriptWithHighlights,
} from "./transcript.js";
import { BroadcastBus, loadSettings, saveSettings } from "./state.js";
import { THEMES } from "./themes.js";

/* ---------- DOM ---------- */
const $ = (id) => document.getElementById(id);
const els = {
  transcript: $("transcript"),
  transcribeBtn: $("transcribeBtn"),
  transcriptStatus: $("transcriptStatus"),
  levelMeter: $("levelMeter").querySelector(".meter-fill"),
  sampleBtn: $("sampleBtn"),
  clearTranscriptBtn: $("clearTranscriptBtn"),

  previewCard: $("previewCard"),
  previewMeta: $("previewMeta"),
  goLiveBtn: $("goLiveBtn"),
  addPreviewToQueueBtn: $("addPreviewToQueueBtn"),

  liveCard: $("liveCard"),
  goLiveSync: $("goLiveSync"),
  onAirBadge: $("onAirBadge"),
  hideLiveBtn: $("hideLiveBtn"),
  forceSyncBtn: $("forceSyncBtn"),

  queueList: $("queueList"),
  queueMeta: $("queueMeta"),
  clearQueueBtn: $("clearQueueBtn"),

  searchModeLabel: $("searchModeLabel"),
  searchBookBtn: $("searchBookBtn"),
  searchContextBtn: $("searchContextBtn"),
  searchBox: $("searchBox"),
  searchBtn: $("searchBtn"),
  searchResults: $("searchResults"),

  detectionsList: $("detectionsList"),
  detectionsMeta: $("detectionsMeta"),
  autoScanCheck: $("autoScanCheck"),

  themeSelect: $("themeSelect"),
  confidenceSlider: $("confidenceSlider"),
  confidenceOut: $("confidenceOut"),
  opacitySlider: $("opacitySlider"),
  opacityOut: $("opacityOut"),
  modeManualBtn: $("modeManualBtn"),
  modeAutoBtn: $("modeAutoBtn"),
  openBroadcastBtn: $("openBroadcastBtn"),
  screenSelect: $("screenSelect"),
  detectScreensBtn: $("detectScreensBtn"),
  fullscreenCheck: $("fullscreenCheck"),
  quitBtn: $("quitBtn"),

  toast: $("toast"),
};

/* ---------- State ---------- */
const settings = loadSettings();
const bus = new BroadcastBus("control");

const state = {
  transcript: "",
  interim: "",
  detections: [], // {payload, confidence, source, timestamp}
  detectionKeys: new Set(),
  queue: [],
  preview: null,
  live: null,
  hidden: false,
  lastAutoLiveAt: 0,
  lastEnterAt: 0,
  searchMode: settings.searchMode || "book",
  displayMode: settings.displayMode || "manual",
  themeId: settings.themeId in THEMES ? settings.themeId : "selah",
  opacity: settings.opacity ?? 1.0,
  confidence: settings.confidenceThreshold ?? 0.6,
  goLive: settings.goLive !== false,
  broadcastOpen: false,
  transcribing: false,
  // Multi-display
  screens: [], // [{key, label, availLeft, availTop, availWidth, availHeight, isPrimary}]
  selectedScreenKey: settings.selectedScreenKey || "popup",
  fullscreenBroadcast: settings.fullscreenBroadcast === true,
  screenDetails: null,
};

const AUTO_COOLDOWN_MS = 2500;

/* ---------- Boot ---------- */
async function boot() {
  await kjvStore.load();
  if (!kjvStore.loaded) {
    toast(`KJV failed to load: ${kjvStore.error ?? "unknown error"}`);
  } else {
    toast("KJV ready (offline). Press Start Transcribing or paste a transcript.");
  }
  hydrateUiFromState();
  bindEvents();
  refreshAll();
  // If the Window Management permission was previously granted, populate
  // the display dropdown silently. Otherwise the user clicks "refresh" to prompt.
  maybeAutoDetectScreens();
}

async function maybeAutoDetectScreens() {
  if (!("getScreenDetails" in window) || !navigator.permissions?.query) return;
  try {
    const status = await navigator.permissions.query({ name: "window-management" });
    if (status.state === "granted") detectScreens(/*interactive*/ false);
    status.onchange = () => {
      if (status.state === "granted") detectScreens(false);
    };
  } catch {
    /* permission name unsupported on this browser; user can still click refresh */
  }
}

function hydrateUiFromState() {
  els.themeSelect.value = state.themeId;
  els.confidenceSlider.value = String(Math.round(state.confidence * 100));
  els.confidenceOut.textContent = `${els.confidenceSlider.value}%`;
  els.opacitySlider.value = String(Math.round(state.opacity * 100));
  els.opacityOut.textContent = `${els.opacitySlider.value}%`;
  els.goLiveSync.checked = state.goLive;
  els.fullscreenCheck.checked = state.fullscreenBroadcast;
  setSearchMode(state.searchMode, /*announce*/ false);
  setDisplayMode(state.displayMode, /*announce*/ false);
  setOnAir(state.goLive && !!state.live);
  paintScreenSelect();
}

/* ---------- Toast ---------- */
let toastTimer = null;
function toast(msg) {
  els.toast.textContent = msg;
  els.toast.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => els.toast.classList.remove("show"), 2400);
}

/* ---------- Event wiring ---------- */
function bindEvents() {
  // Transcript ----------------------------------------------------------------
  els.transcribeBtn.addEventListener("click", toggleTranscribing);
  els.transcript.contentEditable = "true";
  els.transcript.addEventListener("input", () => {
    state.transcript = els.transcript.innerText;
    state.interim = "";
    debouncedScan();
  });
  els.sampleBtn.addEventListener("click", () => {
    const sample =
      "Good morning. Please turn to John chapter 3 verse 16. " +
      "We'll also read Romans 12:1 before we close in Philippians 4:6-7. " +
      "And remember: the Lord is my shepherd, I shall not want.";
    state.transcript = (state.transcript ? state.transcript + " " : "") + sample;
    paintTranscript();
    runScan({ silent: false });
  });
  els.clearTranscriptBtn.addEventListener("click", () => {
    state.transcript = "";
    state.interim = "";
    state.detections = [];
    state.detectionKeys.clear();
    paintTranscript();
    paintDetections();
    toast("Transcript cleared.");
  });

  // Preview / Live ------------------------------------------------------------
  els.goLiveBtn.addEventListener("click", () => goLiveFromPreview());
  els.addPreviewToQueueBtn.addEventListener("click", () => {
    if (!state.preview) return toast("Preview is empty.");
    enqueue(state.preview);
  });
  els.goLiveSync.addEventListener("change", () => {
    state.goLive = els.goLiveSync.checked;
    persist();
    setOnAir(state.goLive && !!state.live);
  });
  els.hideLiveBtn.addEventListener("click", () => {
    state.hidden = true;
    bus.publish({ type: "hide" });
    setOnAir(false);
    toast("Broadcast hidden.");
  });
  els.forceSyncBtn.addEventListener("click", () => {
    if (!state.live) return toast("Nothing live to sync.");
    bus.publish({ type: "theme", themeId: state.themeId });
    bus.publish({ type: "opacity", value: state.opacity });
    bus.publish({ type: "live", payload: state.live });
    state.hidden = false;
    setOnAir(state.goLive);
    toast("Broadcast synced.");
  });

  // Queue ---------------------------------------------------------------------
  els.clearQueueBtn.addEventListener("click", () => {
    if (state.queue.length === 0) return;
    state.queue = [];
    paintQueue();
    toast("Queue cleared.");
  });

  // Search --------------------------------------------------------------------
  els.searchBookBtn.addEventListener("click", () => setSearchMode("book"));
  els.searchContextBtn.addEventListener("click", () => setSearchMode("context"));
  els.searchBtn.addEventListener("click", runSearch);
  els.searchBox.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      const now = performance.now();
      const isDouble = now - state.lastEnterAt < 380;
      state.lastEnterAt = now;
      runSearch({ goLive: isDouble });
    } else if (e.key === "Escape") {
      els.searchBox.blur();
    }
  });

  // Detections ----------------------------------------------------------------
  els.autoScanCheck.addEventListener("change", () => {
    if (els.autoScanCheck.checked) runScan({ silent: true });
  });

  // Top bar -------------------------------------------------------------------
  els.themeSelect.addEventListener("change", () => {
    state.themeId = els.themeSelect.value;
    persist();
    bus.publish({ type: "theme", themeId: state.themeId });
    toast(`Theme: ${THEMES[state.themeId]?.name ?? state.themeId}`);
  });
  els.confidenceSlider.addEventListener("input", () => {
    state.confidence = Number(els.confidenceSlider.value) / 100;
    els.confidenceOut.textContent = `${els.confidenceSlider.value}%`;
    persist();
  });
  els.opacitySlider.addEventListener("input", () => {
    state.opacity = Number(els.opacitySlider.value) / 100;
    els.opacityOut.textContent = `${els.opacitySlider.value}%`;
    bus.publish({ type: "opacity", value: state.opacity });
    persist();
  });
  els.modeManualBtn.addEventListener("click", () => setDisplayMode("manual"));
  els.modeAutoBtn.addEventListener("click", () => setDisplayMode("auto"));
  els.openBroadcastBtn.addEventListener("click", openBroadcastWindow);

  els.screenSelect.addEventListener("change", () => {
    state.selectedScreenKey = els.screenSelect.value;
    persist();
    if (state.broadcastOpen && state.selectedScreenKey !== "popup") {
      sendPlacement();
    }
  });
  els.detectScreensBtn.addEventListener("click", (e) => {
    e.preventDefault();
    detectScreens(/*interactive*/ true);
  });
  els.fullscreenCheck.addEventListener("change", () => {
    state.fullscreenBroadcast = els.fullscreenCheck.checked;
    persist();
    if (state.broadcastOpen) sendPlacement();
  });

  els.quitBtn.addEventListener("click", quitYinka);

  // Global keyboard shortcuts -------------------------------------------------
  document.addEventListener("keydown", (e) => {
    const inEditable =
      e.target.matches("input, textarea") || e.target.isContentEditable;

    if (e.key === "Escape") {
      if (document.activeElement && document.activeElement !== document.body) {
        document.activeElement.blur();
      }
      return;
    }

    if (e.key === "Tab" && !inEditable) {
      e.preventDefault();
      setSearchMode(state.searchMode === "book" ? "context" : "book");
      return;
    }

    if (inEditable) return;

    if (e.key === "l" || e.key === "L") {
      e.preventDefault();
      els.goLiveSync.checked = !els.goLiveSync.checked;
      els.goLiveSync.dispatchEvent(new Event("change"));
      if (els.goLiveSync.checked && state.preview) goLiveFromPreview();
      return;
    }

    // Number keys → jump to verse if a chapter is showing in results.
    if (/^[0-9]$/.test(e.key)) {
      const item = els.searchResults.querySelector('[data-jump-verse="1"]');
      if (item) {
        const targetVerse = Number(e.key);
        item.dispatchEvent(new CustomEvent("jump", { detail: { verse: targetVerse } }));
        e.preventDefault();
      }
    }
  });

  // Bus listener (broadcast window readiness, etc.) --------------------------
  bus.subscribe((msg) => {
    if (msg.type === "broadcast-ready") {
      state.broadcastOpen = true;
      bus.publish({ type: "theme", themeId: state.themeId });
      bus.publish({ type: "opacity", value: state.opacity });
      sendPlacement();
      if (state.live) bus.publish({ type: "live", payload: state.live });
    } else if (msg.type === "broadcast-closing") {
      state.broadcastOpen = false;
    } else if (msg.type === "placement-result") {
      if (msg.error) toast(`Broadcast: ${msg.error}`);
    }
  });
}

/* ---------- Modes ---------- */
function setSearchMode(mode, announce = true) {
  state.searchMode = mode === "context" ? "context" : "book";
  els.searchBookBtn.setAttribute("aria-pressed", state.searchMode === "book");
  els.searchContextBtn.setAttribute("aria-pressed", state.searchMode === "context");
  els.searchBox.placeholder =
    state.searchMode === "book"
      ? "John 3:16 — type a reference (autocompletes books)"
      : "Search by topic, phrase, or paraphrase…";
  els.searchModeLabel.textContent =
    state.searchMode === "book"
      ? "Book mode · press Tab to switch"
      : "Context mode · press Tab to switch";
  persist();
  if (announce) toast(`Search: ${state.searchMode === "book" ? "Book" : "Context"} mode`);
}

function setDisplayMode(mode, announce = true) {
  state.displayMode = mode === "auto" ? "auto" : "manual";
  els.modeManualBtn.setAttribute("aria-pressed", state.displayMode === "manual");
  els.modeAutoBtn.setAttribute("aria-pressed", state.displayMode === "auto");
  persist();
  if (announce) toast(`Display: ${state.displayMode === "auto" ? "Auto" : "Manual"} mode`);
  if (state.displayMode === "auto") tryAutoLive();
}

function persist() {
  saveSettings({
    themeId: state.themeId,
    opacity: state.opacity,
    goLive: state.goLive,
    displayMode: state.displayMode,
    confidenceThreshold: state.confidence,
    searchMode: state.searchMode,
    selectedScreenKey: state.selectedScreenKey,
    fullscreenBroadcast: state.fullscreenBroadcast,
  });
}

/* ---------- Multi-display ---------- */
async function detectScreens(interactive) {
  if (!("getScreenDetails" in window)) {
    if (interactive) {
      toast(
        "This browser doesn't expose extended displays. Use Chrome/Edge 100+ on macOS for multi-monitor placement."
      );
    }
    return;
  }
  try {
    const details = await window.getScreenDetails();
    state.screenDetails = details;
    refreshScreensFromDetails(details);
    details.addEventListener?.("screenschange", () =>
      refreshScreensFromDetails(details)
    );
    if (interactive) {
      const extras = state.screens.length;
      toast(
        extras > 1
          ? `Detected ${extras} display(s). Pick one and click Open Broadcast.`
          : "Only one display detected. Connect an external monitor and refresh."
      );
    }
  } catch (err) {
    if (interactive) {
      toast(
        `Display detection denied: ${err?.message ?? err}. ` +
          "Click the address-bar lock → Site settings → Window Management = Allow."
      );
    }
  }
}

function refreshScreensFromDetails(details) {
  const list = (details.screens ?? []).map((s, i) => ({
    key: `screen-${i}-${Math.round(s.availLeft)}-${Math.round(s.availTop)}`,
    label:
      (s.label && s.label.trim()) ||
      `${s.isPrimary ? "Primary" : "External"} display ${i + 1} · ${Math.round(s.availWidth)}×${Math.round(s.availHeight)}`,
    isPrimary: !!s.isPrimary,
    availLeft: s.availLeft,
    availTop: s.availTop,
    availWidth: s.availWidth,
    availHeight: s.availHeight,
  }));
  state.screens = list;
  paintScreenSelect();
}

function paintScreenSelect() {
  els.screenSelect.innerHTML = "";
  const popup = document.createElement("option");
  popup.value = "popup";
  popup.textContent = "This window (centered popup)";
  els.screenSelect.appendChild(popup);
  for (const s of state.screens) {
    const opt = document.createElement("option");
    opt.value = s.key;
    opt.textContent = `${s.isPrimary ? "★ " : ""}${s.label}`;
    els.screenSelect.appendChild(opt);
  }
  // Restore selection if still present, otherwise auto-pick the first non-primary.
  const persisted = state.selectedScreenKey;
  if (persisted && [...els.screenSelect.options].some((o) => o.value === persisted)) {
    els.screenSelect.value = persisted;
  } else if (state.screens.length > 1) {
    const ext = state.screens.find((s) => !s.isPrimary);
    els.screenSelect.value = ext ? ext.key : "popup";
    state.selectedScreenKey = els.screenSelect.value;
  } else {
    els.screenSelect.value = "popup";
    state.selectedScreenKey = "popup";
  }
}

function selectedScreen() {
  if (state.selectedScreenKey === "popup") return null;
  return state.screens.find((s) => s.key === state.selectedScreenKey) ?? null;
}

function sendPlacement() {
  const screen = selectedScreen();
  bus.publish({
    type: "placement",
    screen,
    fullscreen: !!state.fullscreenBroadcast && !!screen,
  });
}

/* ---------- Transcription ---------- */
let transcriber = null;
let scanTimer = null;

function debouncedScan() {
  if (!els.autoScanCheck.checked) {
    paintTranscript();
    return;
  }
  paintTranscript();
  clearTimeout(scanTimer);
  scanTimer = setTimeout(() => runScan({ silent: true }), 750);
}

function toggleTranscribing() {
  if (state.transcribing) {
    transcriber?.stop();
    return;
  }
  if (!isSpeechSupported()) {
    toast("Live transcription needs Chrome or Edge on macOS. You can paste a transcript instead.");
    return;
  }
  transcriber = createTranscriber({
    onState: (s, detail) => {
      if (s === "starting") {
        els.transcribeBtn.textContent = "Starting…";
        els.transcribeBtn.disabled = true;
        els.transcriptStatus.textContent = "Starting…";
      } else if (s === "listening") {
        state.transcribing = true;
        els.transcribeBtn.textContent = "Stop Transcribing";
        els.transcribeBtn.classList.remove("primary");
        els.transcribeBtn.classList.add("danger");
        els.transcribeBtn.disabled = false;
        els.transcriptStatus.textContent = "Listening";
      } else if (s === "idle") {
        state.transcribing = false;
        els.transcribeBtn.textContent = "Start Transcribing";
        els.transcribeBtn.classList.add("primary");
        els.transcribeBtn.classList.remove("danger");
        els.transcribeBtn.disabled = false;
        els.transcriptStatus.textContent = "Idle";
        els.levelMeter.style.width = "0%";
      } else if (s === "error") {
        state.transcribing = false;
        els.transcribeBtn.textContent = "Start Transcribing";
        els.transcribeBtn.disabled = false;
        els.transcriptStatus.textContent = "Error";
        toast(detail || "Transcription error.");
      }
    },
    onPartial: (text) => {
      state.interim = text;
      paintTranscript();
    },
    onFinal: (text) => {
      state.transcript = (state.transcript ? state.transcript + " " : "") + text;
      state.interim = "";
      paintTranscript();
      runScan({ silent: true });
    },
    onLevel: (lvl) => {
      els.levelMeter.style.width = `${Math.round(lvl * 100)}%`;
    },
  });
  transcriber.start();
}

function paintTranscript() {
  const refs = findReferences(state.transcript);
  // Re-anchor reference indices against the current contentEditable text.
  // findReferences already gives accurate startIndex/endIndex against state.transcript.
  renderTranscriptWithHighlights(els.transcript, state.transcript, refs, state.interim);
  // Move caret to end if focused so typing keeps appending.
  if (document.activeElement === els.transcript) {
    placeCaretAtEnd(els.transcript);
  }
}

function placeCaretAtEnd(el) {
  const range = document.createRange();
  range.selectNodeContents(el);
  range.collapse(false);
  const sel = window.getSelection();
  sel.removeAllRanges();
  sel.addRange(range);
}

/* ---------- Reference scanning + Detections ---------- */
function runScan({ silent }) {
  const refs = findReferences(state.transcript);
  let added = 0;
  for (const r of refs) {
    const payload = kjvStore.getPassage(r);
    if (!payload) continue;
    const key = `${payload.bookSlug}:${payload.chapter}:${payload.verseStart}-${payload.verseEnd}`;
    if (state.detectionKeys.has(key)) continue;
    state.detectionKeys.add(key);
    state.detections.unshift({
      payload,
      confidence: 0.95,
      source: "direct",
      matchedText: r.matchedText,
      timestamp: Date.now(),
    });
    added++;
  }
  if (state.detections.length > 60) state.detections.length = 60;
  paintDetections();
  if (!silent) {
    toast(refs.length === 0 ? "No references found." : `Detected ${refs.length} reference(s).`);
  }
  if (added > 0 && state.displayMode === "auto") tryAutoLive();
}

function tryAutoLive() {
  const top = state.detections.find((d) => d.confidence >= state.confidence);
  if (!top) return;
  const now = Date.now();
  if (now - state.lastAutoLiveAt < AUTO_COOLDOWN_MS) return;
  state.lastAutoLiveAt = now;
  setPreview(top.payload);
  if (state.goLive) goLiveFromPreview({ silent: true });
}

/* ---------- Preview / Live ---------- */
function setPreview(payload) {
  state.preview = payload;
  paintPreview();
  if (state.goLive) {
    setLive(payload, { silent: true });
  }
}

function setLive(payload, { silent } = {}) {
  state.live = payload;
  state.hidden = false;
  paintLive();
  bus.publish({ type: "live", payload });
  setOnAir(state.goLive);
  if (!silent) toast("Live output updated.");
}

function goLiveFromPreview({ silent } = {}) {
  if (!state.preview) {
    if (!silent) toast("Nothing in preview.");
    return;
  }
  setLive(state.preview, { silent });
}

function setOnAir(on) {
  els.onAirBadge.dataset.on = on ? "true" : "false";
  els.onAirBadge.textContent = on ? "ON AIR" : "OFF AIR";
}

/* ---------- Queue ---------- */
function enqueue(payload) {
  const key = `${payload.bookSlug}:${payload.chapter}:${payload.verseStart}-${payload.verseEnd}`;
  if (state.queue.some((q) => q._key === key)) {
    toast("Already in queue.");
    return;
  }
  state.queue.push({ ...payload, _key: key });
  paintQueue();
  toast("Added to queue.");
}

function removeFromQueue(key) {
  state.queue = state.queue.filter((q) => q._key !== key);
  paintQueue();
}

/* ---------- Painters ---------- */
function refreshAll() {
  paintTranscript();
  paintPreview();
  paintLive();
  paintQueue();
  paintDetections();
}

function paintPreview() {
  paintVerseCard(els.previewCard, state.preview);
  els.previewMeta.textContent = state.preview
    ? `${state.preview.bookName} · ${state.preview.translationName}`
    : "—";
}
function paintLive() {
  paintVerseCard(els.liveCard, state.live);
}
function paintVerseCard(card, payload) {
  card.innerHTML = "";
  if (!payload) {
    const empty = document.createElement("div");
    empty.className = "verse-empty";
    empty.textContent = card === els.liveCard ? "Nothing live" : "No verse selected";
    card.appendChild(empty);
    return;
  }
  const ref = document.createElement("div");
  ref.className = "verse-ref";
  ref.textContent = payload.reference;
  const body = document.createElement("div");
  body.className = "verse-body";
  body.textContent = payload.text;
  const trans = document.createElement("div");
  trans.className = "verse-translation";
  trans.textContent = payload.translationName;
  card.appendChild(ref);
  card.appendChild(body);
  card.appendChild(trans);
}

function paintQueue() {
  els.queueList.innerHTML = "";
  els.queueMeta.textContent = `${state.queue.length} verse${state.queue.length === 1 ? "" : "s"}`;
  for (const item of state.queue) {
    const li = document.createElement("li");
    li.className = "queue-item";
    if (state.live && state.live.reference === item.reference && state.live.text === item.text) {
      li.classList.add("live");
    }
    const ref = document.createElement("div");
    ref.className = "qref";
    ref.textContent = item.reference;
    const tools = document.createElement("div");
    tools.className = "row-tools";
    const playBtn = iconButton("▶", "Send to live", () => {
      setPreview(item);
      setLive(item, {});
    }, "go");
    const delBtn = iconButton("✕", "Remove", () => removeFromQueue(item._key), "danger");
    tools.appendChild(playBtn);
    tools.appendChild(delBtn);
    const body = document.createElement("div");
    body.className = "qbody";
    body.textContent = item.text;
    li.appendChild(ref);
    li.appendChild(tools);
    li.appendChild(body);
    li.addEventListener("dblclick", () => setPreview(item));
    els.queueList.appendChild(li);
  }
}

function paintDetections() {
  els.detectionsList.innerHTML = "";
  els.detectionsMeta.textContent =
    state.detections.length === 0
      ? "0 picked up"
      : `${state.detections.length} picked up`;
  for (const d of state.detections) {
    const li = document.createElement("li");
    li.className = "detection-item";
    const ref = document.createElement("div");
    ref.className = "dref";
    ref.textContent = d.payload.reference;
    const tools = document.createElement("div");
    tools.className = "row-tools";
    const conf = document.createElement("span");
    conf.className = "badge confidence";
    conf.textContent = `${Math.round(d.confidence * 100)}%`;
    const src = document.createElement("span");
    src.className = `badge ${d.source === "direct" ? "" : "semantic"}`;
    src.textContent = d.source === "direct" ? "Direct" : "Semantic";
    const time = document.createElement("span");
    time.className = "timestamp";
    time.textContent = formatTime(d.timestamp);
    const playBtn = iconButton("▶", "Display now", () => {
      setPreview(d.payload);
      if (state.goLive) setLive(d.payload, {});
    }, "go");
    const queueBtn = iconButton("+", "Add to queue", () => enqueue(d.payload));
    tools.appendChild(src);
    tools.appendChild(conf);
    tools.appendChild(time);
    tools.appendChild(queueBtn);
    tools.appendChild(playBtn);
    const body = document.createElement("div");
    body.className = "dbody";
    body.textContent = d.payload.text;
    li.appendChild(ref);
    li.appendChild(tools);
    li.appendChild(body);
    li.addEventListener("dblclick", () => setPreview(d.payload));
    els.detectionsList.appendChild(li);
  }
}

function iconButton(label, title, handler, extraClass = "") {
  const b = document.createElement("button");
  b.className = `icon-btn ${extraClass}`.trim();
  b.type = "button";
  b.title = title;
  b.textContent = label;
  b.addEventListener("click", (e) => { e.stopPropagation(); handler(); });
  return b;
}

function formatTime(ms) {
  const d = new Date(ms);
  const hh = d.getHours().toString().padStart(2, "0");
  const mm = d.getMinutes().toString().padStart(2, "0");
  const ss = d.getSeconds().toString().padStart(2, "0");
  return `${hh}:${mm}:${ss}`;
}

/* ---------- Search ---------- */
function runSearch({ goLive: alsoGoLive } = {}) {
  const q = els.searchBox.value.trim();
  els.searchResults.innerHTML = "";
  if (!q) return;
  if (state.searchMode === "book") {
    const r = resolveBookQuery(q);
    if (r.kind === "verse") {
      renderSingleResult(r.payload);
      setPreview(r.payload);
      if (alsoGoLive) {
        els.goLiveSync.checked = true;
        state.goLive = true;
        persist();
        goLiveFromPreview();
      }
    } else if (r.kind === "chapter") {
      renderChapter(r);
    } else if (r.kind === "book") {
      renderInfo(`${r.bookName} · ${r.chapterCount} chapters · type a chapter (e.g. "${r.bookName} 1")`);
    } else if (r.kind === "suggestions") {
      renderSuggestions(r.books);
    } else {
      renderInfo("No match — try “John 3:16” or a book name.");
    }
  } else {
    const matches = contextSearch(q, 12);
    if (matches.length === 0) {
      renderInfo("No verses matched. Try fewer or more distinctive words.");
      return;
    }
    for (const m of matches) renderContextResult(m);
  }
}

function renderSingleResult(payload) {
  const li = document.createElement("li");
  li.className = "result-item";
  const ref = document.createElement("div");
  ref.className = "rref";
  ref.textContent = payload.reference;
  const tools = document.createElement("div");
  tools.className = "row-tools";
  tools.appendChild(iconButton("+", "Queue", () => enqueue(payload)));
  tools.appendChild(iconButton("▶", "Display", () => { setPreview(payload); if (state.goLive) setLive(payload, {}); }, "go"));
  const body = document.createElement("div");
  body.className = "rbody";
  body.textContent = payload.text;
  li.appendChild(ref);
  li.appendChild(tools);
  li.appendChild(body);
  li.addEventListener("dblclick", () => setPreview(payload));
  els.searchResults.appendChild(li);
}

function renderChapter(r) {
  const header = document.createElement("li");
  header.className = "result-item";
  header.dataset.jumpVerse = "1";
  const ref = document.createElement("div");
  ref.className = "rref";
  ref.textContent = `${r.bookName} ${r.chapter} · press 0–9 to jump`;
  header.appendChild(ref);
  els.searchResults.appendChild(header);

  for (const v of r.verses) {
    const li = document.createElement("li");
    li.className = "result-item";
    const ref = document.createElement("div");
    ref.className = "rref";
    ref.textContent = `${r.bookName} ${r.chapter}:${v.verse}`;
    const tools = document.createElement("div");
    tools.className = "row-tools";
    const payload = {
      reference: `${r.bookName} ${r.chapter}:${v.verse}`,
      bookName: r.bookName,
      bookSlug: r.bookSlug,
      chapter: r.chapter,
      verseStart: v.verse,
      verseEnd: v.verse,
      text: v.text,
      translationId: "kjv",
      translationName: "King James Version",
    };
    tools.appendChild(iconButton("+", "Queue", () => enqueue(payload)));
    tools.appendChild(iconButton("▶", "Display", () => { setPreview(payload); if (state.goLive) setLive(payload, {}); }, "go"));
    const body = document.createElement("div");
    body.className = "rbody";
    body.textContent = v.text;
    li.appendChild(ref);
    li.appendChild(tools);
    li.appendChild(body);
    li.addEventListener("dblclick", () => setPreview(payload));
    els.searchResults.appendChild(li);
  }

  header.addEventListener("jump", (e) => {
    const verse = e.detail?.verse;
    if (!verse) return;
    const target = els.searchResults.children[verse]; // header is index 0
    target?.scrollIntoView({ behavior: "smooth", block: "center" });
  });
}

function renderInfo(text) {
  const li = document.createElement("li");
  li.className = "result-item";
  const body = document.createElement("div");
  body.className = "rbody";
  body.textContent = text;
  li.appendChild(body);
  els.searchResults.appendChild(li);
}

function renderSuggestions(books) {
  if (!books.length) return renderInfo("No matching books.");
  const li = document.createElement("li");
  li.className = "result-item";
  const ref = document.createElement("div");
  ref.className = "rref";
  ref.textContent = "Did you mean…";
  const body = document.createElement("div");
  body.className = "rbody";
  body.textContent = books.map((b) => b.name).join(" · ");
  li.appendChild(ref);
  li.appendChild(body);
  li.addEventListener("click", () => {
    if (books[0]) {
      els.searchBox.value = books[0].name + " ";
      els.searchBox.focus();
    }
  });
  els.searchResults.appendChild(li);
}

function renderContextResult({ payload, score }) {
  const li = document.createElement("li");
  li.className = "result-item";
  const ref = document.createElement("div");
  ref.className = "rref";
  ref.textContent = payload.reference;
  const tools = document.createElement("div");
  tools.className = "row-tools";
  const conf = document.createElement("span");
  conf.className = "badge confidence";
  conf.textContent = `${score}%`;
  tools.appendChild(conf);
  tools.appendChild(iconButton("+", "Queue", () => enqueue(payload)));
  tools.appendChild(iconButton("▶", "Display", () => { setPreview(payload); if (state.goLive) setLive(payload, {}); }, "go"));
  const body = document.createElement("div");
  body.className = "rbody";
  body.textContent = payload.text;
  li.appendChild(ref);
  li.appendChild(tools);
  li.appendChild(body);
  li.addEventListener("dblclick", () => setPreview(payload));
  els.searchResults.appendChild(li);
}

/* ---------- Broadcast window ---------- */
let broadcastWindow = null;
function openBroadcastWindow() {
  if (broadcastWindow && !broadcastWindow.closed) {
    broadcastWindow.focus();
    sendPlacement();
    return;
  }
  const screen = selectedScreen();

  let features;
  if (screen) {
    features =
      `popup=yes,noopener=no,width=${Math.round(screen.availWidth)},` +
      `height=${Math.round(screen.availHeight)},` +
      `left=${Math.round(screen.availLeft)},top=${Math.round(screen.availTop)}`;
  } else {
    const w = 1280, h = 720;
    const sx = Math.max(0, (window.screen.width - w) / 2);
    const sy = Math.max(0, (window.screen.height - h) / 3);
    features = `popup=yes,noopener=no,width=${w},height=${h},left=${sx},top=${sy}`;
  }

  broadcastWindow = window.open("broadcast.html", "yinkaBroadcast", features);
  if (!broadcastWindow) {
    toast("Browser blocked the popup. Allow popups for this site, then click again.");
    return;
  }
  if (screen) {
    toast(
      `Broadcast opened on “${screen.label}”${state.fullscreenBroadcast ? " — click the popup once to enter fullscreen." : "."}`
    );
  } else {
    toast("Broadcast window opened. Add it as Window Capture in OBS.");
  }
}

/* ---------- Quit ---------- */
async function quitYinka() {
  const ok = confirm(
    "Stop the Yinka server and quit the app?\n\nThe broadcast window will close. You can reopen by relaunching Yinka."
  );
  if (!ok) return;
  try {
    if (broadcastWindow && !broadcastWindow.closed) broadcastWindow.close();
  } catch { /* ignore */ }
  try { transcriber?.stop(); } catch { /* ignore */ }
  try {
    await fetch("/__quit", { cache: "no-store" });
  } catch {
    /* server is going down — losing the response is expected */
  }
  document.body.innerHTML =
    '<div style="display:flex;align-items:center;justify-content:center;height:100vh;font:14px -apple-system,Inter,sans-serif;color:#8a8f99;background:#0b0d10;text-align:center;padding:20px;">' +
      '<div><div style="font-size:18px;color:#f3f5f8;margin-bottom:8px;">Yinka stopped.</div>' +
      '<div>You can close this tab. Relaunch Yinka.app to start again.</div></div>' +
    '</div>';
}

/* ---------- Go ---------- */
boot();
