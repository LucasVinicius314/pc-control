// The parts of the previews that need no DOM: reading pictures and cursor messages, remembering the
// settings and deciding which messages to send. The wire format is in docs/protocol/websocket.md.

export type Channel = "magnifier" | "display";
export type Corner = "tl" | "tr" | "bl" | "br";

export const MAGNIFIER_KIND = 0x10;
export const DISPLAY_KIND = 0x11;
export const CURSOR_KIND = 0x12;
export const HEADER_LENGTH = 6;
export const CURSOR_LENGTH = 6;
export const NO_MONITOR = 0xff;

export interface Frame {
  channel: Channel;
  /** Index of the monitor this picture shows. */
  monitor: number;
  /** Where the mouse cursor is inside the picture, or -1 when it is not in it. */
  cursorX: number;
  cursorY: number;
  jpeg: Uint8Array;
}

export interface CursorUpdate {
  /** Index of the monitor the cursor is on, or -1 when it is on none. */
  monitor: number;
  /** Position inside that monitor as a fraction from 0 to 1. */
  x: number;
  y: number;
}

/** Splits a binary message from the PC into its header and the JPEG. Null if it is not a picture. */
export function parseFrame(data: ArrayBuffer): Frame | null {
  if (data.byteLength <= HEADER_LENGTH) return null;
  const view = new DataView(data);
  const kind = view.getUint8(0);
  if (kind !== MAGNIFIER_KIND && kind !== DISPLAY_KIND) return null;

  return {
    channel: kind === MAGNIFIER_KIND ? "magnifier" : "display",
    monitor: view.getUint8(1),
    cursorX: view.getInt16(2, true),
    cursorY: view.getInt16(4, true),
    jpeg: new Uint8Array(data, HEADER_LENGTH),
  };
}

/** Reads the small cursor message the PC sends on its own, many times a second. */
export function parseCursor(data: ArrayBuffer): CursorUpdate | null {
  if (data.byteLength !== CURSOR_LENGTH) return null;
  const view = new DataView(data);
  if (view.getUint8(0) !== CURSOR_KIND) return null;

  const monitor = view.getUint8(1);
  return {
    monitor: monitor === NO_MONITOR ? -1 : monitor,
    x: view.getUint16(2, true) / 65535,
    y: view.getUint16(4, true) / 65535,
  };
}

export interface ViewPrefs {
  display: boolean;
  /** Pictures per second for the display preview. The cursor ring is not limited by this. */
  displayFps: number;
  mini: boolean;
  /** How many phone pixels one PC pixel takes up in the mini preview. */
  miniZoom: number;
  corner: Corner;
  /** Monitor index, or -1 to follow the cursor. */
  monitor: number;
}

export const DEFAULT_PREFS: ViewPrefs = { display: false, displayFps: 10, mini: false, miniZoom: 1, corner: "tr", monitor: -1 };
export const MINI_ZOOMS = [0.5, 1, 1.5] as const;
export const DISPLAY_FPS = [3, 6, 10] as const;
const CORNERS: Corner[] = ["tr", "br", "bl", "tl"];

/** The mini preview is this many CSS pixels; the zoom decides how much of the PC screen it covers. */
export const MINI_CSS_WIDTH = 280;
export const MINI_CSS_HEIGHT = 160;

export function miniRegion(zoom: number): { width: number; height: number } {
  // Below 1 the preview is zoomed out: it covers more of the PC screen than it has room to show.
  const z = Math.max(0.5, zoom);
  return { width: Math.round(MINI_CSS_WIDTH / z), height: Math.round(MINI_CSS_HEIGHT / z) };
}

export function nextCorner(corner: Corner): Corner {
  return CORNERS[(CORNERS.indexOf(corner) + 1) % CORNERS.length] ?? "tr";
}

/** Accepts whatever was stored and returns safe settings, so old or damaged data cannot break the page. */
export function sanitizePrefs(raw: unknown): ViewPrefs {
  const r = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
  const zoom = Number(r["miniZoom"]);
  const monitor = Number(r["monitor"]);
  const fps = Number(r["displayFps"]);
  return {
    display: r["display"] === true,
    displayFps: (DISPLAY_FPS as readonly number[]).includes(fps) ? fps : DEFAULT_PREFS.displayFps,
    mini: r["mini"] === true,
    miniZoom: (MINI_ZOOMS as readonly number[]).includes(zoom) ? zoom : DEFAULT_PREFS.miniZoom,
    corner: CORNERS.includes(r["corner"] as Corner) ? (r["corner"] as Corner) : DEFAULT_PREFS.corner,
    monitor: Number.isInteger(monitor) && monitor >= 0 && monitor <= 15 ? monitor : -1,
  };
}

export interface ViewContext {
  /** The WebSocket is open. */
  online: boolean;
  /** The page is on screen. Hidden pages must not make the PC capture its screen. */
  visible: boolean;
  /** Width of the trackpad area in CSS pixels. */
  padWidth: number;
  devicePixelRatio: number;
}

/**
 * The display preview is as wide as the phone can show it, within sensible limits. It is capped low
 * on purpose: it is for orientation, the mini preview is for detail, and smaller pictures arrive sooner.
 */
export function displayWidth(padWidth: number, devicePixelRatio: number): number {
  return Math.max(320, Math.min(800, Math.round(padWidth * Math.max(1, devicePixelRatio))));
}

/** The `view` messages that make the PC stream what the settings ask for, and nothing else. */
export function buildViewMessages(prefs: ViewPrefs, ctx: ViewContext): Record<string, unknown>[] {
  const active = ctx.online && ctx.visible;
  const region = miniRegion(prefs.miniZoom);

  return [
    {
      type: "view",
      stream: "magnifier",
      enabled: active && prefs.mini,
      monitor: prefs.monitor,
      width: region.width,
      height: region.height,
      fps: 15,
    },
    {
      type: "view",
      stream: "display",
      enabled: active && prefs.display,
      monitor: prefs.monitor,
      width: displayWidth(ctx.padWidth, ctx.devicePixelRatio),
      fps: prefs.displayFps,
    },
  ];
}

export interface Box {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** Where a picture of the given size lands inside a box when it is scaled to fit (object-fit: contain). */
export function containedRect(boxWidth: number, boxHeight: number, imageWidth: number, imageHeight: number): Box {
  if (imageWidth <= 0 || imageHeight <= 0 || boxWidth <= 0 || boxHeight <= 0) {
    return { left: 0, top: 0, width: 0, height: 0 };
  }
  const scale = Math.min(boxWidth / imageWidth, boxHeight / imageHeight);
  const width = imageWidth * scale;
  const height = imageHeight * scale;
  return { left: (boxWidth - width) / 2, top: (boxHeight - height) / 2, width, height };
}

export const STATUS_NOTES: Record<string, string> = {
  locked: "The PC screen is locked",
  paused: "Paused on the PC",
  failed: "Can't read the PC screen",
};
