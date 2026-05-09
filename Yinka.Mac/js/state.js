// Cross-window state and broadcast channel. The control window publishes
// `live`, `theme`, `opacity`, and `hide` events; broadcast.html subscribes
// to render Pewbeam-style verse cards with whatever theme is active.

const CHANNEL_NAME = "yinka-broadcast";
const STORAGE_KEY = "yinka:settings:v1";

export class BroadcastBus {
  constructor(role) {
    this.role = role;
    this.channel = "BroadcastChannel" in window ? new BroadcastChannel(CHANNEL_NAME) : null;
    this.listeners = new Set();
    if (this.channel) {
      this.channel.onmessage = (event) => {
        for (const fn of this.listeners) fn(event.data);
      };
    } else {
      // Fallback: localStorage event for older browsers
      window.addEventListener("storage", (e) => {
        if (e.key !== `${CHANNEL_NAME}:msg` || !e.newValue) return;
        try {
          const data = JSON.parse(e.newValue);
          for (const fn of this.listeners) fn(data);
        } catch { /* ignore */ }
      });
    }
  }

  publish(message) {
    if (this.channel) {
      this.channel.postMessage(message);
      return;
    }
    try {
      localStorage.setItem(`${CHANNEL_NAME}:msg`, JSON.stringify({ ...message, _t: Date.now() }));
    } catch { /* ignore */ }
  }

  subscribe(fn) {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  }
}

const DEFAULT_SETTINGS = {
  themeId: "selah",
  opacity: 1.0,
  goLive: true,
  displayMode: "manual", // 'manual' | 'auto'
  confidenceThreshold: 0.6, // 0..1
  searchMode: "book", // 'book' | 'context'
  showRefAbove: false,
};

export function loadSettings() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { ...DEFAULT_SETTINGS };
    return { ...DEFAULT_SETTINGS, ...JSON.parse(raw) };
  } catch {
    return { ...DEFAULT_SETTINGS };
  }
}

export function saveSettings(settings) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  } catch { /* ignore */ }
}
