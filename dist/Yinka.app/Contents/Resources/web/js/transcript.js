// Live transcription via the Web Speech API + an input-level meter
// driven by the Web Audio API (AnalyserNode). Chrome-on-macOS supports
// `webkitSpeechRecognition`; Safari does not. The UI degrades gracefully
// when speech recognition is missing.

const Speech =
  window.SpeechRecognition || window.webkitSpeechRecognition || null;

export function isSpeechSupported() {
  return !!Speech;
}

/**
 * @param {{
 *   onPartial: (text:string) => void,
 *   onFinal:   (text:string) => void,
 *   onState:   (state:'idle'|'starting'|'listening'|'error', detail?:string) => void,
 *   onLevel:   (level01:number) => void,
 * }} cb
 */
export function createTranscriber(cb) {
  let recog = null;
  let stream = null;
  let audioCtx = null;
  let analyser = null;
  let levelTimer = null;
  let restartGuard = false;
  let stopping = false;

  async function start() {
    if (!Speech) {
      cb.onState("error", "Speech recognition isn't available in this browser. Open Yinka in Chrome or Edge.");
      return;
    }
    cb.onState("starting");

    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
      cb.onState("error", `Microphone permission denied (${err.message ?? err}).`);
      return;
    }

    audioCtx = new (window.AudioContext || window.webkitAudioContext)();
    const src = audioCtx.createMediaStreamSource(stream);
    analyser = audioCtx.createAnalyser();
    analyser.fftSize = 1024;
    src.connect(analyser);
    const buf = new Uint8Array(analyser.fftSize);
    levelTimer = setInterval(() => {
      analyser.getByteTimeDomainData(buf);
      let sum = 0;
      for (let i = 0; i < buf.length; i++) {
        const v = (buf[i] - 128) / 128;
        sum += v * v;
      }
      const rms = Math.sqrt(sum / buf.length);
      cb.onLevel(Math.min(1, rms * 2.4));
    }, 90);

    recog = new Speech();
    recog.lang = "en-US";
    recog.continuous = true;
    recog.interimResults = true;

    recog.onresult = (event) => {
      let interim = "";
      let finals = "";
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const r = event.results[i];
        const text = r[0]?.transcript ?? "";
        if (r.isFinal) finals += text + " ";
        else interim += text;
      }
      if (finals) cb.onFinal(finals.trim());
      if (interim) cb.onPartial(interim.trim());
    };

    recog.onerror = (event) => {
      // Auto-recover from transient "no-speech" / network blips while listening.
      if (event.error === "no-speech" || event.error === "audio-capture") return;
      cb.onState("error", `Speech recognition error: ${event.error}`);
    };

    recog.onend = () => {
      if (stopping) return;
      // Chrome stops after ~60s of silence; restart if the user is still listening.
      if (!restartGuard) {
        restartGuard = true;
        try { recog.start(); } catch { /* swallow */ }
        setTimeout(() => (restartGuard = false), 250);
      }
    };

    try {
      recog.start();
      cb.onState("listening");
    } catch (err) {
      cb.onState("error", `Could not start speech recognition (${err.message ?? err}).`);
    }
  }

  function stop() {
    stopping = true;
    try { recog && recog.stop(); } catch { /* ignore */ }
    recog = null;

    if (levelTimer) {
      clearInterval(levelTimer);
      levelTimer = null;
    }
    if (analyser) {
      try { analyser.disconnect(); } catch { /* ignore */ }
      analyser = null;
    }
    if (audioCtx) {
      try { audioCtx.close(); } catch { /* ignore */ }
      audioCtx = null;
    }
    if (stream) {
      stream.getTracks().forEach((t) => t.stop());
      stream = null;
    }
    cb.onLevel(0);
    cb.onState("idle");
    stopping = false;
  }

  return { start, stop };
}

/**
 * Render plain transcript text + a list of detected reference ranges
 * into an element, highlighting each reference span. Avoids innerHTML
 * with raw user text by escaping segments.
 */
export function renderTranscriptWithHighlights(el, text, refs, interimTail = "") {
  el.textContent = "";
  if (!text && !interimTail) return;

  const ranges = [...(refs || [])]
    .filter((r) => r.startIndex != null && r.endIndex != null)
    .sort((a, b) => a.startIndex - b.startIndex);

  let cursor = 0;
  for (const r of ranges) {
    if (r.startIndex < cursor) continue; // skip overlaps
    if (r.startIndex > cursor) {
      el.appendChild(document.createTextNode(text.slice(cursor, r.startIndex)));
    }
    const span = document.createElement("mark");
    span.className = "ref-hit";
    span.textContent = text.slice(r.startIndex, r.endIndex);
    span.title = r.matchedText;
    el.appendChild(span);
    cursor = r.endIndex;
  }
  if (cursor < text.length) {
    el.appendChild(document.createTextNode(text.slice(cursor)));
  }
  if (interimTail) {
    const tail = document.createElement("span");
    tail.className = "interim";
    tail.textContent = (text ? " " : "") + interimTail;
    el.appendChild(tail);
  }
  const cursorEl = document.createElement("span");
  cursorEl.className = "cursor-blink";
  el.appendChild(cursorEl);
}
