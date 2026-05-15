// Built-in themes for the broadcast window. Each theme exposes a CSS
// variable bundle plus optional layout hints. Pewbeam-style: Selah is
// a warm dark theme with gold accents; Eden is a green/teal gradient.

export const THEMES = {
  selah: {
    id: "selah",
    name: "Selah",
    description: "Warm dark theme with gold accents",
    vars: {
      "--bg": "radial-gradient(circle at 30% 20%, #2a1d11 0%, #100806 60%, #050302 100%)",
      "--fg": "#F4E4BC",
      "--accent": "#D4AF37",
      "--ref-color": "#E8C77A",
      "--translation-color": "rgba(244,228,188,0.6)",
      "--font-verse": "'Cormorant Garamond', Georgia, 'Times New Roman', serif",
      "--font-ref": "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
      "--verse-weight": "500",
      "--verse-shadow": "0 4px 24px rgba(0,0,0,0.55)",
    },
  },
  eden: {
    id: "eden",
    name: "Eden",
    description: "Gradient background with green accents",
    vars: {
      "--bg": "linear-gradient(135deg, #0b3d2e 0%, #134e3a 50%, #1a6b4e 100%)",
      "--fg": "#F0FFF4",
      "--accent": "#95D5B2",
      "--ref-color": "#B8E0D2",
      "--translation-color": "rgba(240,255,244,0.6)",
      "--font-verse": "'Inter', -apple-system, BlinkMacSystemFont, 'Helvetica Neue', sans-serif",
      "--font-ref": "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
      "--verse-weight": "400",
      "--verse-shadow": "0 4px 28px rgba(0,0,0,0.45)",
    },
  },
  obsKey: {
    id: "obsKey",
    name: "OBS Chroma Key",
    description: "Pure green background for OBS Color Key",
    vars: {
      "--bg": "#00FF00",
      "--fg": "#0A0A0A",
      "--accent": "#0A2A12",
      "--ref-color": "#0A2A12",
      "--translation-color": "rgba(10,10,10,0.7)",
      "--font-verse": "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
      "--font-ref": "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
      "--verse-weight": "600",
      "--verse-shadow": "none",
    },
  },
  pulpit: {
    id: "pulpit",
    name: "Pulpit (Projector)",
    description: "Pure black with large gold reference — designed for projectors and TVs as the secondary display",
    vars: {
      "--bg": "#000000",
      "--fg": "#FFFFFF",
      "--accent": "#E8C77A",
      "--ref-color": "#E8C77A",
      "--translation-color": "rgba(255,255,255,0.45)",
      "--font-verse": "'Cormorant Garamond', Georgia, 'Times New Roman', serif",
      "--font-ref": "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
      "--verse-weight": "500",
      "--verse-shadow": "0 6px 32px rgba(0,0,0,0.85)",
    },
  },
};

export function applyTheme(rootEl, themeId) {
  const theme = THEMES[themeId] ?? THEMES.selah;
  for (const [key, value] of Object.entries(theme.vars)) {
    rootEl.style.setProperty(key, value);
  }
  rootEl.dataset.theme = theme.id;
}

export function listThemes() {
  return Object.values(THEMES);
}
