// Yinka — Pewbeam-style control window. Wires the dashboard panels,
// mic transcription, reference detection, search, queue, themes, and the
// broadcast (OBS) window via BroadcastChannel.

import { findReferences } from "./parser.js";
import { kjvStore } from "./store.js";
import { resolveBookQuery, contextSearch, resolveBookSlug } from "./search.js";
import { HYMNS, searchHymns } from "./hymns.js";
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
  liveScreenSelect: $("liveScreenSelect"),
  liveDetectScreensBtn: $("liveDetectScreensBtn"),
  liveFullscreenCheck: $("liveFullscreenCheck"),
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
  popoutLiveBtn: $("popoutLiveBtn"),
  screenSelect: $("screenSelect"),
  detectScreensBtn: $("detectScreensBtn"),
  fullscreenCheck: $("fullscreenCheck"),
  quitBtn: $("quitBtn"),

  // Bible Reader
  bibleMeta: $("bibleMeta"),
  bibleBooksOT: $("bibleBooksOT"),
  bibleBooksNT: $("bibleBooksNT"),
  bibleChaptersTitle: $("bibleChaptersTitle"),
  bibleChapterGrid: $("bibleChapterGrid"),
  bibleVersesTitle: $("bibleVersesTitle"),
  bibleVerseList: $("bibleVerseList"),
  bibleQuickJump: $("bibleQuickJump"),
  bibleClearSelectionBtn: $("bibleClearSelectionBtn"),
  biblePresentRangeBtn: $("biblePresentRangeBtn"),
  bibleQueueRangeBtn: $("bibleQueueRangeBtn"),

  // Hymns
  hymnMeta: $("hymnMeta"),
  hymnSearch: $("hymnSearch"),
  hymnResults: $("hymnResults"),
  hymnTitle: $("hymnTitle"),
  hymnStanzas: $("hymnStanzas"),
  hymnDisplayAllBtn: $("hymnDisplayAllBtn"),
  hymnQueueAllBtn: $("hymnQueueAllBtn"),

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
  // Bible reader
  bible: {
    activeBookSlug: settings.bibleBookSlug || null,
    activeChapter: settings.bibleChapter || null,
    rangeStart: null, // 1-indexed verse number
    rangeEnd: null,
  },
  hymn: {
    activeId: settings.hymnId || null,
    activeStanza: settings.hymnStanza || null,
  },
};

const AUTO_COOLDOWN_MS = 2500;

/* ---------- Boot ---------- */
async function boot() {
  await kjvStore.load();
  if (!kjvStore.loaded) {
    toast(`KJV failed to load: ${kjvStore.error ?? "unknown error"}`);
  } else {
    toast("KJV ready (offline). Press Start Transcribing or open the Bible panel below.");
  }
  hydrateUiFromState();
  bindEvents();
  refreshAll();
  paintHymnResults(HYMNS);
  if (state.hymn.activeId) selectHymn(state.hymn.activeId, /*silent*/ true);
  paintBibleBooks();
  // Restore last viewed book/chapter, if any.
  if (state.bible.activeBookSlug) {
    selectBook(state.bible.activeBookSlug, /*silent*/ true);
    if (state.bible.activeChapter) {
      selectChapter(state.bible.activeChapter, /*silent*/ true);
    }
  }
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
  els.liveFullscreenCheck.checked = state.fullscreenBroadcast;
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
  els.popoutLiveBtn.addEventListener("click", openBroadcastWindow);

  // Bible reader -------------------------------------------------------------
  els.bibleQuickJump.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      bibleQuickJump(els.bibleQuickJump.value);
    } else if (e.key === "Escape") {
      els.bibleQuickJump.blur();
    }
  });
  els.bibleClearSelectionBtn.addEventListener("click", clearBibleRange);
  els.biblePresentRangeBtn.addEventListener("click", () => {
    const p = bibleRangePayload();
    if (!p) return;
    setPreview(p);
    setLive(p, {});
    toast(`Now displaying ${p.reference}.`);
  });
  els.bibleQueueRangeBtn.addEventListener("click", () => {
    const p = bibleRangePayload();
    if (!p) return;
    enqueue(p);
  });

  // Hymns --------------------------------------------------------------------
  els.hymnSearch.addEventListener("input", () => {
    paintHymnResults(searchHymns(els.hymnSearch.value));
  });
  els.hymnSearch.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      const first = searchHymns(els.hymnSearch.value)[0];
      if (first) selectHymn(first.id);
    } else if (e.key === "Escape") {
      els.hymnSearch.value = "";
      paintHymnResults(HYMNS);
      els.hymnSearch.blur();
    }
  });
  els.hymnDisplayAllBtn.addEventListener("click", () => {
    const p = activeHymnPayload("all");
    if (!p) return;
    setPreview(p);
    setLive(p, {});
  });
  els.hymnQueueAllBtn.addEventListener("click", () => {
    const p = activeHymnPayload("all");
    if (!p) return;
    enqueue(p);
  });

  els.screenSelect.addEventListener("change", () => {
    setSelectedScreenKey(els.screenSelect.value);
  });
  els.liveScreenSelect.addEventListener("change", () => {
    setSelectedScreenKey(els.liveScreenSelect.value);
  });
  els.detectScreensBtn.addEventListener("click", (e) => {
    e.preventDefault();
    detectScreens(/*interactive*/ true);
  });
  els.liveDetectScreensBtn.addEventListener("click", (e) => {
    e.preventDefault();
    detectScreens(/*interactive*/ true);
  });
  els.fullscreenCheck.addEventListener("change", () => {
    setFullscreenBroadcast(els.fullscreenCheck.checked);
  });
  els.liveFullscreenCheck.addEventListener("change", () => {
    setFullscreenBroadcast(els.liveFullscreenCheck.checked);
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
    bibleBookSlug: state.bible.activeBookSlug,
    bibleChapter: state.bible.activeChapter,
    hymnId: state.hymn.activeId,
    hymnStanza: state.hymn.activeStanza,
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
  paintOneScreenSelect(els.screenSelect);
  paintOneScreenSelect(els.liveScreenSelect);
}

function paintOneScreenSelect(selectEl) {
  selectEl.innerHTML = "";
  const popup = document.createElement("option");
  popup.value = "popup";
  popup.textContent = "This window (centered popup)";
  selectEl.appendChild(popup);
  for (const s of state.screens) {
    const opt = document.createElement("option");
    opt.value = s.key;
    opt.textContent = `${s.isPrimary ? "★ " : ""}${s.label}`;
    selectEl.appendChild(opt);
  }
  // Restore selection if still present, otherwise auto-pick the first non-primary.
  const persisted = state.selectedScreenKey;
  if (persisted && [...selectEl.options].some((o) => o.value === persisted)) {
    selectEl.value = persisted;
  } else if (state.screens.length > 1) {
    const ext = state.screens.find((s) => !s.isPrimary);
    selectEl.value = ext ? ext.key : "popup";
    state.selectedScreenKey = selectEl.value;
  } else {
    selectEl.value = "popup";
    state.selectedScreenKey = "popup";
  }
}

function setSelectedScreenKey(key) {
  state.selectedScreenKey = key;
  if (els.screenSelect.value !== key) els.screenSelect.value = key;
  if (els.liveScreenSelect.value !== key) els.liveScreenSelect.value = key;
  persist();
  if (state.broadcastOpen) sendPlacement();
}

function setFullscreenBroadcast(on) {
  state.fullscreenBroadcast = !!on;
  els.fullscreenCheck.checked = state.fullscreenBroadcast;
  els.liveFullscreenCheck.checked = state.fullscreenBroadcast;
  persist();
  if (state.broadcastOpen) sendPlacement();
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
  paintBibleVerseHighlights();
  if (state.goLive) {
    setLive(payload, { silent: true });
  }
}

function setLive(payload, { silent } = {}) {
  state.live = payload;
  state.hidden = false;
  paintLive();
  paintBibleVerseHighlights();
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

/* ---------- Hymns ---------- */
function paintHymnResults(hymns) {
  els.hymnResults.innerHTML = "";
  if (!hymns.length) {
    const empty = document.createElement("li");
    empty.className = "hymn-empty";
    empty.textContent = "No hymns matched.";
    els.hymnResults.appendChild(empty);
    return;
  }
  for (const hymn of hymns) {
    const li = document.createElement("li");
    li.className = "hymn-item";
    li.dataset.hymnId = hymn.id;
    li.setAttribute("aria-selected", hymn.id === state.hymn.activeId ? "true" : "false");
    const title = document.createElement("div");
    title.className = "hymn-item-title";
    title.textContent = hymn.title;
    const meta = document.createElement("div");
    meta.className = "hymn-item-meta";
    meta.textContent = `${hymn.author} · ${hymn.year} · ${hymn.license}`;
    li.appendChild(title);
    li.appendChild(meta);
    li.addEventListener("click", () => selectHymn(hymn.id));
    els.hymnResults.appendChild(li);
  }
}

function selectHymn(id, silent = false) {
  const hymn = HYMNS.find((h) => h.id === id);
  if (!hymn) return;
  state.hymn.activeId = hymn.id;
  state.hymn.activeStanza = Math.min(hymn.stanzas.length, state.hymn.activeStanza ?? 1);
  persist();
  for (const li of els.hymnResults.querySelectorAll(".hymn-item")) {
    li.setAttribute("aria-selected", li.dataset.hymnId === hymn.id ? "true" : "false");
  }
  els.hymnTitle.textContent = `${hymn.title} · ${hymn.author} (${hymn.year})`;
  els.hymnMeta.textContent = `${hymn.title} · ${hymn.stanzas.length} stanzas · ${hymn.license}`;
  els.hymnDisplayAllBtn.disabled = false;
  els.hymnQueueAllBtn.disabled = false;
  paintHymnStanzas(hymn);
  if (!silent) setPreview(hymnPayload(hymn, state.hymn.activeStanza, state.hymn.activeStanza));
}

function paintHymnStanzas(hymn) {
  els.hymnStanzas.innerHTML = "";
  const frag = document.createDocumentFragment();
  hymn.stanzas.forEach((text, i) => {
    const stanzaNum = i + 1;
    const li = document.createElement("li");
    li.className = "hymn-stanza";
    li.dataset.stanza = String(stanzaNum);
    li.setAttribute("aria-selected", stanzaNum === state.hymn.activeStanza ? "true" : "false");
    const num = document.createElement("div");
    num.className = "hymn-stanza-num";
    num.textContent = `S${stanzaNum}`;
    const body = document.createElement("div");
    body.className = "hymn-stanza-text";
    body.textContent = text;
    const tools = document.createElement("div");
    tools.className = "hymn-stanza-tools";
    tools.appendChild(iconButton("+", "Queue stanza", (e) => {
      e?.stopPropagation();
      enqueue(hymnPayload(hymn, stanzaNum, stanzaNum));
    }));
    tools.appendChild(iconButton("▶", "Display stanza", (e) => {
      e?.stopPropagation();
      const p = hymnPayload(hymn, stanzaNum, stanzaNum);
      setPreview(p);
      if (state.goLive) setLive(p, {});
    }, "go"));
    li.addEventListener("click", () => selectHymnStanza(hymn, stanzaNum));
    li.addEventListener("dblclick", () => {
      const p = hymnPayload(hymn, stanzaNum, stanzaNum);
      setPreview(p);
      setLive(p, {});
    });
    li.appendChild(num);
    li.appendChild(body);
    li.appendChild(tools);
    frag.appendChild(li);
  });
  els.hymnStanzas.appendChild(frag);
}

function selectHymnStanza(hymn, stanzaNum) {
  state.hymn.activeId = hymn.id;
  state.hymn.activeStanza = stanzaNum;
  persist();
  for (const li of els.hymnStanzas.querySelectorAll(".hymn-stanza")) {
    li.setAttribute("aria-selected", li.dataset.stanza === String(stanzaNum) ? "true" : "false");
  }
  setPreview(hymnPayload(hymn, stanzaNum, stanzaNum));
}

function activeHymnPayload(scope) {
  const hymn = HYMNS.find((h) => h.id === state.hymn.activeId);
  if (!hymn) {
    toast("Pick a hymn first.");
    return null;
  }
  if (scope === "all") return hymnPayload(hymn, 1, hymn.stanzas.length);
  const stanza = state.hymn.activeStanza ?? 1;
  return hymnPayload(hymn, stanza, stanza);
}

function hymnPayload(hymn, start, end) {
  const lo = Math.max(1, Math.min(start, end));
  const hi = Math.min(hymn.stanzas.length, Math.max(start, end));
  return {
    reference: lo === hi ? `${hymn.title} · Stanza ${lo}` : `${hymn.title} · Stanzas ${lo}-${hi}`,
    bookName: hymn.title,
    bookSlug: `hymn:${hymn.id}`,
    chapter: 1,
    verseStart: lo,
    verseEnd: hi,
    text: hymn.stanzas.slice(lo - 1, hi).join("\n\n"),
    translationId: "hymn",
    translationName: `${hymn.license} hymn`,
  };
}

/* ---------- Bible Reader ---------- */
// Old Testament = first 39 canonical books, New Testament = last 27.
const OT_COUNT = 39;

function paintBibleBooks() {
  const books = kjvStore.listBooks();
  if (!books.length) {
    els.bibleBooksOT.innerHTML = '<li class="bible-empty">KJV not loaded.</li>';
    els.bibleBooksNT.innerHTML = "";
    return;
  }
  const ot = books.slice(0, OT_COUNT);
  const nt = books.slice(OT_COUNT);
  els.bibleBooksOT.innerHTML = "";
  els.bibleBooksNT.innerHTML = "";
  for (const b of ot) els.bibleBooksOT.appendChild(makeBookLi(b));
  for (const b of nt) els.bibleBooksNT.appendChild(makeBookLi(b));
}

function makeBookLi(book) {
  const li = document.createElement("li");
  li.textContent = book.name;
  li.dataset.slug = book.slug;
  li.title = `${book.name} · ${book.chapterCount} chapter${book.chapterCount === 1 ? "" : "s"}`;
  li.addEventListener("click", () => selectBook(book.slug));
  return li;
}

function selectBook(slug, silent = false) {
  const books = kjvStore.listBooks();
  const book = books.find((b) => b.slug === slug);
  if (!book) return;
  state.bible.activeBookSlug = slug;
  state.bible.activeChapter = null;
  state.bible.rangeStart = null;
  state.bible.rangeEnd = null;
  persist();

  for (const li of els.bibleBooksOT.querySelectorAll("li")) {
    li.setAttribute("aria-selected", li.dataset.slug === slug ? "true" : "false");
  }
  for (const li of els.bibleBooksNT.querySelectorAll("li")) {
    li.setAttribute("aria-selected", li.dataset.slug === slug ? "true" : "false");
  }

  paintBibleChapters(book);
  els.bibleVerseList.innerHTML = "";
  els.bibleVersesTitle.textContent = `${book.name} · pick a chapter`;
  els.bibleMeta.textContent = `${book.name} · ${book.chapterCount} chapters`;
  paintBibleRangeButtons();
  if (!silent) {
    // Auto-pick chapter 1 so the user immediately sees verses on click.
    selectChapter(1);
  }
}

function paintBibleChapters(book) {
  els.bibleChaptersTitle.textContent = `${book.name} · ${book.chapterCount} chapters`;
  els.bibleChapterGrid.innerHTML = "";
  for (let c = 1; c <= book.chapterCount; c++) {
    const tile = document.createElement("button");
    tile.type = "button";
    tile.className = "chapter-tile";
    tile.textContent = String(c);
    tile.dataset.chapter = String(c);
    tile.title = `${book.name} ${c}`;
    tile.addEventListener("click", () => selectChapter(c));
    els.bibleChapterGrid.appendChild(tile);
  }
}

function selectChapter(chapter, silent = false) {
  const slug = state.bible.activeBookSlug;
  if (!slug) return;
  const verses = kjvStore.chapterVerses(slug, chapter);
  if (verses.length === 0) {
    toast("Chapter not found.");
    return;
  }
  state.bible.activeChapter = chapter;
  state.bible.rangeStart = null;
  state.bible.rangeEnd = null;
  persist();

  const books = kjvStore.listBooks();
  const book = books.find((b) => b.slug === slug);
  const bookName = book?.name ?? slug;

  for (const tile of els.bibleChapterGrid.querySelectorAll(".chapter-tile")) {
    tile.setAttribute(
      "aria-selected",
      tile.dataset.chapter === String(chapter) ? "true" : "false"
    );
  }

  els.bibleVersesTitle.textContent = `${bookName} ${chapter} · ${verses.length} verses`;
  paintBibleVerses(bookName, slug, chapter, verses);
  paintBibleRangeButtons();
  if (!silent) els.bibleVerseList.scrollTop = 0;
}

function paintBibleVerses(bookName, slug, chapter, verses) {
  els.bibleVerseList.innerHTML = "";
  const frag = document.createDocumentFragment();
  for (let i = 0; i < verses.length; i++) {
    const verseNum = i + 1;
    const text = verses[i];
    const li = document.createElement("li");
    li.className = "bible-verse-row";
    li.dataset.verse = String(verseNum);
    li.dataset.bookSlug = slug;
    li.dataset.chapter = String(chapter);
    li.title = `Click ${bookName} ${chapter}:${verseNum} to preview · Shift-click to extend a range · Double-click to go live`;

    const num = document.createElement("div");
    num.className = "bible-verse-num";
    num.textContent = `${verseNum}`;

    const body = document.createElement("div");
    body.className = "bible-verse-text";
    body.textContent = text;

    const tools = document.createElement("div");
    tools.className = "bible-verse-tools";
    const queueBtn = iconButton("+", "Add to queue", (e) => {
      e?.stopPropagation();
      enqueue(verseToPayload(slug, bookName, chapter, verseNum, verseNum));
    });
    const playBtn = iconButton("▶", "Display now", (e) => {
      e?.stopPropagation();
      const p = verseToPayload(slug, bookName, chapter, verseNum, verseNum);
      setPreview(p);
      if (state.goLive) setLive(p, {});
    }, "go");
    tools.appendChild(queueBtn);
    tools.appendChild(playBtn);

    li.addEventListener("click", (e) => onBibleVerseClick(verseNum, e));
    li.addEventListener("dblclick", (e) => {
      e.preventDefault();
      const p = verseToPayload(slug, bookName, chapter, verseNum, verseNum);
      setPreview(p);
      setLive(p, {});
    });

    li.appendChild(num);
    li.appendChild(body);
    li.appendChild(tools);
    frag.appendChild(li);
  }
  els.bibleVerseList.appendChild(frag);
  paintBibleVerseHighlights();
}

function verseToPayload(slug, bookName, chapter, start, end) {
  const verses = kjvStore.chapterVerses(slug, chapter);
  const lo = Math.min(start, end);
  const hi = Math.max(start, end);
  const text = verses.slice(lo - 1, hi).join(" ");
  const reference =
    lo === hi
      ? `${bookName} ${chapter}:${lo}`
      : `${bookName} ${chapter}:${lo}-${hi}`;
  return {
    reference,
    bookName,
    bookSlug: slug,
    chapter,
    verseStart: lo,
    verseEnd: hi,
    text,
    translationId: "kjv",
    translationName: "King James Version",
  };
}

function onBibleVerseClick(verseNum, e) {
  const slug = state.bible.activeBookSlug;
  const chapter = state.bible.activeChapter;
  if (!slug || !chapter) return;
  const books = kjvStore.listBooks();
  const book = books.find((b) => b.slug === slug);
  const bookName = book?.name ?? slug;

  if (e.shiftKey && state.bible.rangeStart != null) {
    state.bible.rangeEnd = verseNum;
  } else {
    state.bible.rangeStart = verseNum;
    state.bible.rangeEnd = verseNum;
  }
  paintBibleVerseHighlights();
  paintBibleRangeButtons();

  // Single click → set preview to the (possibly multi-verse) selection.
  const p = verseToPayload(
    slug,
    bookName,
    chapter,
    state.bible.rangeStart,
    state.bible.rangeEnd ?? state.bible.rangeStart
  );
  setPreview(p);
}

function clearBibleRange() {
  state.bible.rangeStart = null;
  state.bible.rangeEnd = null;
  paintBibleVerseHighlights();
  paintBibleRangeButtons();
}

function bibleRangePayload() {
  const slug = state.bible.activeBookSlug;
  const chapter = state.bible.activeChapter;
  if (!slug || !chapter || state.bible.rangeStart == null) {
    toast("Select a verse (or shift-click for a range) first.");
    return null;
  }
  const books = kjvStore.listBooks();
  const book = books.find((b) => b.slug === slug);
  const bookName = book?.name ?? slug;
  return verseToPayload(
    slug,
    bookName,
    chapter,
    state.bible.rangeStart,
    state.bible.rangeEnd ?? state.bible.rangeStart
  );
}

function paintBibleRangeButtons() {
  const has = state.bible.rangeStart != null;
  const lo = state.bible.rangeStart;
  const hi = state.bible.rangeEnd ?? state.bible.rangeStart;
  const isRange = has && lo !== hi;
  els.bibleClearSelectionBtn.hidden = !has;
  els.biblePresentRangeBtn.hidden = !isRange;
  els.bibleQueueRangeBtn.hidden = !isRange;
  if (isRange) {
    const lo2 = Math.min(lo, hi);
    const hi2 = Math.max(lo, hi);
    const count = hi2 - lo2 + 1;
    els.biblePresentRangeBtn.textContent = `Display range (${count})`;
    els.bibleQueueRangeBtn.textContent = `+ Queue range (${count})`;
  }
}

function paintBibleVerseHighlights() {
  const slug = state.bible.activeBookSlug;
  const chapter = state.bible.activeChapter;
  if (!slug || !chapter) return;
  const lo = state.bible.rangeStart != null
    ? Math.min(state.bible.rangeStart, state.bible.rangeEnd ?? state.bible.rangeStart)
    : null;
  const hi = state.bible.rangeStart != null
    ? Math.max(state.bible.rangeStart, state.bible.rangeEnd ?? state.bible.rangeStart)
    : null;

  const liveLo =
    state.live && state.live.bookSlug === slug && state.live.chapter === chapter
      ? state.live.verseStart
      : null;
  const liveHi =
    state.live && state.live.bookSlug === slug && state.live.chapter === chapter
      ? state.live.verseEnd
      : null;

  const previewLo =
    state.preview && state.preview.bookSlug === slug && state.preview.chapter === chapter
      ? state.preview.verseStart
      : null;
  const previewHi =
    state.preview && state.preview.bookSlug === slug && state.preview.chapter === chapter
      ? state.preview.verseEnd
      : null;

  for (const li of els.bibleVerseList.querySelectorAll(".bible-verse-row")) {
    const v = Number(li.dataset.verse);
    li.classList.remove("in-range", "is-live", "is-preview");
    li.removeAttribute("aria-selected");
    if (lo != null && v >= lo && v <= hi) {
      if (v === lo && v === hi) li.setAttribute("aria-selected", "true");
      else li.classList.add("in-range");
    }
    if (liveLo != null && v >= liveLo && v <= liveHi) li.classList.add("is-live");
    if (previewLo != null && v >= previewLo && v <= previewHi && !li.classList.contains("is-live")) {
      li.classList.add("is-preview");
    }
  }
}

function bibleQuickJump(query) {
  const q = (query ?? "").trim();
  if (!q) return;
  // First try a full reference (John 3:16, Ps 23:1-3, etc.).
  const refs = findReferences(q);
  if (refs.length > 0) {
    const r = refs[0];
    selectBook(r.bookSlug, /*silent*/ true);
    selectChapter(r.chapter);
    state.bible.rangeStart = r.verseStart;
    state.bible.rangeEnd = r.verseEnd ?? r.verseStart;
    paintBibleVerseHighlights();
    paintBibleRangeButtons();
    scrollVerseIntoView(state.bible.rangeStart);
    const p = bibleRangePayload();
    if (p) setPreview(p);
    return;
  }
  // Try "book chapter" form (John 3, Ps 23).
  const bookChapter = /^([1-3]?\s?[a-z][a-z\s.]+?)\s+(\d+)\s*$/i.exec(q);
  if (bookChapter) {
    const slug = resolveBookSlug(bookChapter[1].trim().replace(/\./g, ""));
    if (slug) {
      selectBook(slug, /*silent*/ true);
      selectChapter(parseInt(bookChapter[2], 10));
      return;
    }
  }
  // Just a book name.
  const slug = resolveBookSlug(q);
  if (slug) {
    selectBook(slug);
    return;
  }
  toast("Couldn't find a book matching that. Try “John 3:16” or “Romans”.");
}

function scrollVerseIntoView(verseNum) {
  const li = els.bibleVerseList.querySelector(`[data-verse="${verseNum}"]`);
  if (li) li.scrollIntoView({ behavior: "smooth", block: "center" });
}

/* ---------- Go ---------- */
boot();
