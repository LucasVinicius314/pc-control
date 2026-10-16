import { describe, expect, it } from "vitest";
import { contrast, deriveColors, luminance, parseHex, readableAccent, textOn, toHex } from "./theme";

describe("colour parsing", () => {
  it("reads and writes #rrggbb", () => {
    expect(parseHex("#0078d4")).toEqual([0, 120, 212]);
    expect(parseHex(" #FFFFFF ")).toEqual([255, 255, 255]);
    expect(toHex([0, 120, 212])).toBe("#0078d4");
  });

  it("refuses anything else", () => {
    expect(parseHex("red")).toBeNull();
    expect(parseHex("#fff")).toBeNull();
    expect(parseHex("")).toBeNull();
    expect(parseHex("#12345g")).toBeNull();
  });

  it("clamps when writing", () => {
    expect(toHex([-5, 300, 12.4])).toBe("#00ff0c");
  });
});

describe("contrast", () => {
  it("is 21 for black on white and 1 for the same colour", () => {
    expect(contrast([0, 0, 0], [255, 255, 255])).toBeCloseTo(21, 0);
    expect(contrast([10, 20, 30], [10, 20, 30])).toBeCloseTo(1, 5);
  });

  it("orders luminance from black to white", () => {
    expect(luminance([0, 0, 0])).toBe(0);
    expect(luminance([255, 255, 255])).toBeCloseTo(1, 5);
  });
});

describe("readableAccent", () => {
  const dark: [number, number, number] = [14, 17, 22];
  const light: [number, number, number] = [241, 243, 246];

  it("leaves a colour that already shows up alone", () => {
    expect(readableAccent([90, 169, 255], dark)).toEqual([90, 169, 255]);
  });

  it("lightens an accent that disappears into a dark background", () => {
    const navy: [number, number, number] = [10, 20, 60];
    const fixed = readableAccent(navy, dark);

    expect(contrast(navy, dark)).toBeLessThan(3);
    expect(contrast(fixed, dark)).toBeGreaterThanOrEqual(3);
  });

  it("darkens an accent that disappears into a light background", () => {
    const yellow: [number, number, number] = [255, 235, 120];
    const fixed = readableAccent(yellow, light);

    expect(contrast(yellow, light)).toBeLessThan(3);
    expect(contrast(fixed, light)).toBeGreaterThanOrEqual(3);
  });
});

describe("textOn", () => {
  it("picks dark text on bright colours and white on dark ones", () => {
    expect(textOn([255, 235, 120])).toBe("#0a0f16");
    expect(textOn([10, 20, 60])).toBe("#ffffff");
  });
});

describe("deriveColors", () => {
  it("builds the dark palette around the accent", () => {
    const colors = deriveColors({ accent: "#5aa9ff", dark: true });

    expect(colors.bg).toBe("#0e1116");
    expect(colors.accent).toBe("#5aa9ff");
    expect(colors.onAccent).toBe("#0a0f16");
  });

  it("builds a light palette for light mode", () => {
    const colors = deriveColors({ accent: "#0078d4", dark: false });

    expect(colors.bg).not.toBe("#0e1116");
    expect(luminance(parseHex(colors.bg)!)).toBeGreaterThan(0.5);
    expect(luminance(parseHex(colors.text)!)).toBeLessThan(0.2);
  });

  it("always gives text that can be read on the accent", () => {
    for (const hex of ["#c74e40", "#0078d4", "#107c10", "#ffb900", "#000000", "#ffffff"]) {
      for (const dark of [true, false]) {
        const colors = deriveColors({ accent: hex, dark });
        expect(contrast(parseHex(colors.accent)!, parseHex(colors.onAccent)!)).toBeGreaterThanOrEqual(4.2); // the worst case is a mid-tone, where neither text colour is great
      }
    }
  });

  it("falls back to the default blue for an unreadable accent", () => {
    expect(deriveColors({ accent: "nonsense", dark: true }).accent).toBe("#5aa9ff");
  });
});
