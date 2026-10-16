// Follows the PC's Windows theme: its accent colour and dark or light mode. The colour maths is plain
// functions (tested); only applyTheme touches the page.

export interface PcTheme {
  /** "#rrggbb". */
  accent: string;
  dark: boolean;
}

export interface ThemeColors {
  bg: string;
  panel: string;
  line: string;
  text: string;
  muted: string;
  accent: string;
  /** Text and icons drawn on top of the accent colour. */
  onAccent: string;
  ok: string;
  warn: string;
  bad: string;
}

type Rgb = [number, number, number];

const DARK: Omit<ThemeColors, "accent" | "onAccent"> = {
  bg: "#0e1116",
  panel: "#171c24",
  line: "#2a3340",
  text: "#e8edf3",
  muted: "#8b97a7",
  ok: "#4cc38a",
  warn: "#f0b429",
  bad: "#ff6b6b",
};

const LIGHT: Omit<ThemeColors, "accent" | "onAccent"> = {
  bg: "#f1f3f6",
  panel: "#ffffff",
  line: "#d3d9e1",
  text: "#18202b",
  muted: "#586474",
  ok: "#17794a",
  warn: "#8a5a00",
  bad: "#c42b31",
};

export const DEFAULT_THEME: PcTheme = { accent: "#5aa9ff", dark: true };

export function parseHex(hex: string): Rgb | null {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex.trim());
  if (!match) return null;
  return [parseInt(match[1]!, 16), parseInt(match[2]!, 16), parseInt(match[3]!, 16)];
}

export function toHex([r, g, b]: Rgb): string {
  const part = (n: number) => Math.round(Math.min(255, Math.max(0, n))).toString(16).padStart(2, "0");
  return `#${part(r)}${part(g)}${part(b)}`;
}

/** WCAG relative luminance, 0 (black) to 1 (white). */
export function luminance([r, g, b]: Rgb): number {
  const channel = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** WCAG contrast ratio, 1 (none) to 21 (black on white). */
export function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

function mix(from: Rgb, to: Rgb, amount: number): Rgb {
  return [0, 1, 2].map((i) => from[i]! + (to[i]! - from[i]!) * amount) as Rgb;
}

/**
 * The accent, adjusted so it can be seen on the page background: a very dark accent on the dark theme
 * (or a very light one on the light theme) is moved towards white (or black) until it reaches the
 * contrast needed for icons and outlines.
 */
export function readableAccent(accent: Rgb, background: Rgb, minimum = 3): Rgb {
  const towards: Rgb = luminance(background) < 0.5 ? [255, 255, 255] : [0, 0, 0];
  let result = accent;
  for (let step = 1; step <= 20 && contrast(result, background) < minimum; step++) {
    result = mix(accent, towards, step / 20);
  }
  return result;
}

/** Black or white, whichever reads better on the colour. */
export function textOn(colour: Rgb): string {
  const dark: Rgb = [10, 15, 22];
  return contrast(colour, dark) >= contrast(colour, [255, 255, 255]) ? toHex(dark) : "#ffffff";
}

/** The whole palette for a theme. An accent that cannot be read falls back to the default blue. */
export function deriveColors(theme: PcTheme): ThemeColors {
  const base = theme.dark ? DARK : LIGHT;
  const parsed = parseHex(theme.accent) ?? parseHex(DEFAULT_THEME.accent)!;
  const accent = readableAccent(parsed, parseHex(base.bg)!);
  return { ...base, accent: toHex(accent), onAccent: textOn(accent) };
}

const CACHE_KEY = "pc-control.theme";

export function loadCachedTheme(): PcTheme | null {
  try {
    const data = JSON.parse(localStorage.getItem(CACHE_KEY) ?? "null") as Partial<PcTheme> | null;
    if (data && typeof data.accent === "string" && typeof data.dark === "boolean" && parseHex(data.accent)) {
      return { accent: data.accent, dark: data.dark };
    }
  } catch {
    // No saved theme; the default shows until the PC answers.
  }
  return null;
}

function saveTheme(theme: PcTheme): void {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(theme));
  } catch {
    // Not remembered; the next start asks the PC again.
  }
}

/** Puts the theme on the page: colour variables, the browser's colour scheme, and the status bar colour. */
export function applyTheme(theme: PcTheme, remember = true): void {
  const colors = deriveColors(theme);
  const root = document.documentElement;
  const vars: Record<string, string> = {
    "--bg": colors.bg,
    "--panel": colors.panel,
    "--line": colors.line,
    "--text": colors.text,
    "--muted": colors.muted,
    "--accent": colors.accent,
    "--on-accent": colors.onAccent,
    "--ok": colors.ok,
    "--warn": colors.warn,
    "--bad": colors.bad,
  };
  for (const [name, value] of Object.entries(vars)) root.style.setProperty(name, value);
  root.style.setProperty("color-scheme", theme.dark ? "dark" : "light");
  root.dataset["theme"] = theme.dark ? "dark" : "light";
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", colors.bg);
  document.querySelector('meta[name="color-scheme"]')?.setAttribute("content", theme.dark ? "dark" : "light");
  if (remember) saveTheme(theme);
}
