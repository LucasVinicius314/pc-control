import { describe, expect, it } from "vitest";
import { diffComposition } from "./textdiff";

describe("diffComposition", () => {
  it("typing more adds the new tail", () => {
    expect(diffComposition("hel", "hell")).toEqual({ backspaces: 0, insert: "l" });
  });

  it("deleting removes characters", () => {
    expect(diffComposition("hello", "hel")).toEqual({ backspaces: 2, insert: "" });
  });

  it("an autocorrected word replaces only the part that differs", () => {
    expect(diffComposition("teh", "the")).toEqual({ backspaces: 2, insert: "he" });
  });

  it("a suggestion that extends the word types the rest", () => {
    expect(diffComposition("tom", "tomorrow")).toEqual({ backspaces: 0, insert: "orrow" });
  });

  it("nothing changed means nothing to send", () => {
    expect(diffComposition("same", "same")).toEqual({ backspaces: 0, insert: "" });
  });

  it("starting from empty types everything, and clearing deletes everything", () => {
    expect(diffComposition("", "abc")).toEqual({ backspaces: 0, insert: "abc" });
    expect(diffComposition("abc", "")).toEqual({ backspaces: 3, insert: "" });
  });

  it("counts an emoji as one character, not two UTF-16 units", () => {
    expect(diffComposition("a😀", "a")).toEqual({ backspaces: 1, insert: "" });
    expect(diffComposition("a", "a😀")).toEqual({ backspaces: 0, insert: "😀" });
  });

  it("handles accented text", () => {
    expect(diffComposition("cafe", "café")).toEqual({ backspaces: 1, insert: "é" });
  });
});
