import { describe, expect, it } from "vitest";
import { easeOut, glideMs } from "./smoothscroll";

describe("easeOut", () => {
  it("runs from 0 to 1 and never overshoots", () => {
    expect(easeOut(0)).toBe(0);
    expect(easeOut(1)).toBe(1);
    expect(easeOut(-3)).toBe(0);
    expect(easeOut(9)).toBe(1);
  });

  it("starts fast and settles", () => {
    expect(easeOut(0.25)).toBeGreaterThan(0.25);
    expect(easeOut(0.9) - easeOut(0.8)).toBeLessThan(easeOut(0.2) - easeOut(0.1));
  });
});

describe("glideMs", () => {
  it("is longer for longer jumps, within limits", () => {
    expect(glideMs(40)).toBeLessThan(glideMs(400));
    expect(glideMs(-400)).toBe(glideMs(400));
    expect(glideMs(100000)).toBe(360);
    expect(glideMs(0)).toBeGreaterThanOrEqual(150);
  });
});
