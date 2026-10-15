import { describe, expect, it } from "vitest";
import {
  buildViewMessages,
  containedRect,
  CURSOR_KIND,
  DEFAULT_PREFS,
  DISPLAY_KIND,
  displayWidth,
  MAGNIFIER_KIND,
  MINI_ZOOMS,
  miniRegion,
  nextCorner,
  parseCursor,
  parseFrame,
  sanitizePrefs,
  type ViewContext,
  type ViewPrefs,
} from "./viewlogic";

function frame(kind: number, x: number, y: number, jpegBytes: number[], monitor = 0): ArrayBuffer {
  const buffer = new ArrayBuffer(6 + jpegBytes.length);
  const view = new DataView(buffer);
  view.setUint8(0, kind);
  view.setUint8(1, monitor);
  view.setInt16(2, x, true);
  view.setInt16(4, y, true);
  new Uint8Array(buffer, 6).set(jpegBytes);
  return buffer;
}

function cursor(monitor: number, x: number, y: number): ArrayBuffer {
  const buffer = new ArrayBuffer(6);
  const view = new DataView(buffer);
  view.setUint8(0, CURSOR_KIND);
  view.setUint8(1, monitor);
  view.setUint16(2, Math.round(x * 65535), true);
  view.setUint16(4, Math.round(y * 65535), true);
  return buffer;
}

describe("parseFrame", () => {
  it("reads the channel, the monitor, the cursor position and the picture", () => {
    const parsed = parseFrame(frame(DISPLAY_KIND, 300, -1, [0xff, 0xd8, 0xff], 2));

    expect(parsed?.channel).toBe("display");
    expect(parsed?.monitor).toBe(2);
    expect(parsed?.cursorX).toBe(300);
    expect(parsed?.cursorY).toBe(-1);
    expect([...(parsed?.jpeg ?? [])]).toEqual([0xff, 0xd8, 0xff]);
  });

  it("tells the mini preview apart", () => {
    expect(parseFrame(frame(MAGNIFIER_KIND, 1, 2, [1]))?.channel).toBe("magnifier");
  });

  it("reads negative and large positions", () => {
    const parsed = parseFrame(frame(DISPLAY_KIND, -32768, 32767, [1]));

    expect([parsed?.cursorX, parsed?.cursorY]).toEqual([-32768, 32767]);
  });

  it("refuses short frames and unknown kinds", () => {
    expect(parseFrame(new ArrayBuffer(0))).toBeNull();
    expect(parseFrame(frame(DISPLAY_KIND, 0, 0, []))).toBeNull(); // header only
    expect(parseFrame(frame(0x42, 0, 0, [1]))).toBeNull();
  });

  it("does not mistake a cursor message for a picture", () => {
    expect(parseFrame(cursor(0, 0.5, 0.5))).toBeNull();
  });
});

describe("parseCursor", () => {
  it("reads the monitor and the position as fractions", () => {
    const parsed = parseCursor(cursor(1, 0.25, 0.75));

    expect(parsed?.monitor).toBe(1);
    expect(parsed?.x).toBeCloseTo(0.25, 4);
    expect(parsed?.y).toBeCloseTo(0.75, 4);
  });

  it("reaches both ends of the range", () => {
    expect(parseCursor(cursor(0, 0, 0))).toMatchObject({ x: 0, y: 0 });
    expect(parseCursor(cursor(0, 1, 1))).toMatchObject({ x: 1, y: 1 });
  });

  it("says -1 when the cursor is on no monitor", () => {
    expect(parseCursor(cursor(0xff, 0, 0))?.monitor).toBe(-1);
  });

  it("refuses anything that is not a cursor message", () => {
    expect(parseCursor(new ArrayBuffer(0))).toBeNull();
    expect(parseCursor(new ArrayBuffer(7))).toBeNull();
    expect(parseCursor(frame(DISPLAY_KIND, 0, 0, [1, 2, 3, 4, 5, 6, 7]))).toBeNull();
    const wrongKind = cursor(0, 0.5, 0.5);
    new DataView(wrongKind).setUint8(0, 0x42);
    expect(parseCursor(wrongKind)).toBeNull();
  });
});

describe("sanitizePrefs", () => {
  it("keeps good values", () => {
    const good: ViewPrefs = { display: true, displayFps: 6, mini: true, miniZoom: 1.5, corner: "bl", monitor: 1 };

    expect(sanitizePrefs(good)).toEqual(good);
  });

  it("falls back to the defaults for junk", () => {
    expect(sanitizePrefs(null)).toEqual(DEFAULT_PREFS);
    expect(sanitizePrefs("nope")).toEqual(DEFAULT_PREFS);
    expect(sanitizePrefs({ display: "yes", displayFps: 999, miniZoom: 99, corner: "middle", monitor: -4 })).toEqual(DEFAULT_PREFS);
  });

  it("rejects a monitor index that is not a small whole number", () => {
    expect(sanitizePrefs({ monitor: 1.5 }).monitor).toBe(-1);
    expect(sanitizePrefs({ monitor: 99 }).monitor).toBe(-1);
    expect(sanitizePrefs({ monitor: 2 }).monitor).toBe(2);
  });

  it("keeps only the offered refresh rates", () => {
    expect(sanitizePrefs({ displayFps: 3 }).displayFps).toBe(3);
    expect(sanitizePrefs({ miniZoom: 3 }).miniZoom).toBe(DEFAULT_PREFS.miniZoom); // an old setting that no longer exists
    expect(sanitizePrefs({ displayFps: 7 }).displayFps).toBe(DEFAULT_PREFS.displayFps);
  });
});

describe("miniRegion", () => {
  it("covers less of the screen at higher zoom", () => {
    expect(miniRegion(1).width).toBeGreaterThan(miniRegion(1.5).width);
    expect(miniRegion(0.5).width).toBeGreaterThan(miniRegion(1).width);
    expect(miniRegion(1).height).toBeGreaterThan(miniRegion(1.5).height);
  });

  it("at 1x shows exactly as many PC pixels as the preview has room for, at 0.5x twice as many", () => {
    expect(miniRegion(1)).toEqual({ width: 280, height: 160 });
    expect(miniRegion(0.5)).toEqual({ width: 560, height: 320 });
    expect(miniRegion(1.5)).toEqual({ width: 187, height: 107 });
  });

  it("keeps the phone's aspect ratio", () => {
    for (const zoom of [0.5, 1, 1.5]) {
      const r = miniRegion(zoom);

      expect(r.width / r.height).toBeCloseTo(280 / 160, 1);
    }
  });

  it("never asks for an absurdly large region", () => {
    expect(miniRegion(0.01)).toEqual(miniRegion(0.5));
    expect(miniRegion(0.5).width).toBeLessThanOrEqual(800);
  });

  it("offers only 0.5x, 1x and 1.5x", () => {
    expect([...MINI_ZOOMS]).toEqual([0.5, 1, 1.5]);
  });
});

describe("nextCorner", () => {
  it("visits all four corners and comes back", () => {
    const seen = new Set<string>();
    let c = DEFAULT_PREFS.corner;
    for (let i = 0; i < 4; i++) {
      seen.add(c);
      c = nextCorner(c);
    }

    expect(seen.size).toBe(4);
    expect(c).toBe(DEFAULT_PREFS.corner);
  });
});

describe("displayWidth", () => {
  it("matches the screen, within limits", () => {
    expect(displayWidth(300, 2)).toBe(600);
    expect(displayWidth(100, 1)).toBe(320);
    expect(displayWidth(900, 3)).toBe(800);
  });
});

describe("containedRect", () => {
  it("centres a wide picture in a tall box", () => {
    expect(containedRect(400, 800, 1000, 500)).toEqual({ left: 0, top: 300, width: 400, height: 200 });
  });

  it("centres a tall picture in a wide box", () => {
    expect(containedRect(800, 400, 500, 1000)).toEqual({ left: 300, top: 0, width: 200, height: 400 });
  });

  it("fills the box when the shapes match", () => {
    expect(containedRect(400, 200, 800, 400)).toEqual({ left: 0, top: 0, width: 400, height: 200 });
  });

  it("scales small pictures up to fit", () => {
    const r = containedRect(400, 400, 100, 100);

    expect([r.width, r.height]).toEqual([400, 400]);
  });

  it("is empty for empty sizes", () => {
    expect(containedRect(0, 100, 10, 10).width).toBe(0);
    expect(containedRect(100, 100, 0, 10).width).toBe(0);
  });
});

describe("buildViewMessages", () => {
  const ctx: ViewContext = { online: true, visible: true, padWidth: 400, devicePixelRatio: 2 };
  const find = (messages: Record<string, unknown>[], stream: string) => messages.find((m) => m["stream"] === stream);

  it("turns on only what the settings ask for", () => {
    const messages = buildViewMessages({ ...DEFAULT_PREFS, mini: true }, ctx);

    expect(find(messages, "magnifier")?.["enabled"]).toBe(true);
    expect(find(messages, "display")?.["enabled"]).toBe(false);
  });

  it("asks for a region that matches the zoom, and a display as wide as the phone", () => {
    const messages = buildViewMessages({ ...DEFAULT_PREFS, mini: true, display: true, miniZoom: 0.5, monitor: 1, displayFps: 6 }, ctx);

    expect(find(messages, "magnifier")).toMatchObject({ width: 560, height: 320, monitor: 1, fps: 15 });
    expect(find(messages, "display")).toMatchObject({ width: 800, monitor: 1, fps: 6 });
  });

  it("turns everything off while the page is hidden", () => {
    const messages = buildViewMessages({ ...DEFAULT_PREFS, mini: true, display: true }, { ...ctx, visible: false });

    expect(messages.every((m) => m["enabled"] === false)).toBe(true);
  });

  it("turns everything off while offline", () => {
    const messages = buildViewMessages({ ...DEFAULT_PREFS, mini: true, display: true }, { ...ctx, online: false });

    expect(messages.every((m) => m["enabled"] === false)).toBe(true);
  });

  it("always sends both channels, so a stale stream is always switched off", () => {
    expect(buildViewMessages(DEFAULT_PREFS, ctx)).toHaveLength(2);
  });
});
