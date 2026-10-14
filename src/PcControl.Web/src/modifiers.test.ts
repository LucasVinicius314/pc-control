import { beforeEach, describe, expect, it } from "vitest";
import { charToCode, Modifiers } from "./modifiers";

let sent: string[];
let changes: number;
let mods: Modifiers;

beforeEach(() => {
  sent = [];
  changes = 0;
  mods = new Modifiers(
    (code, down) => sent.push(`${code} ${down ? "down" : "up"}`),
    () => changes++,
  );
});

describe("Modifiers", () => {
  it("one tap holds the modifier for the next action only", () => {
    mods.toggle("ControlLeft");
    expect(mods.state("ControlLeft")).toBe("once");
    expect(mods.anyHeld).toBe(true);

    mods.consumeOnce();

    expect(sent).toEqual(["ControlLeft down", "ControlLeft up"]);
    expect(mods.state("ControlLeft")).toBe("off");
    expect(mods.anyHeld).toBe(false);
  });

  it("a second tap locks it, and locked modifiers survive an action", () => {
    mods.toggle("ShiftLeft");
    mods.toggle("ShiftLeft");
    mods.consumeOnce();

    expect(mods.state("ShiftLeft")).toBe("locked");
    expect(sent).toEqual(["ShiftLeft down"]);
  });

  it("a third tap lets go", () => {
    mods.toggle("AltLeft");
    mods.toggle("AltLeft");
    mods.toggle("AltLeft");

    expect(mods.state("AltLeft")).toBe("off");
    expect(sent).toEqual(["AltLeft down", "AltLeft up"]);
  });

  it("combines modifiers: Ctrl once plus Shift locked", () => {
    mods.toggle("ControlLeft");
    mods.toggle("ShiftLeft");
    mods.toggle("ShiftLeft");
    mods.consumeOnce();

    expect(mods.state("ControlLeft")).toBe("off");
    expect(mods.state("ShiftLeft")).toBe("locked");
    expect(sent).toEqual(["ControlLeft down", "ShiftLeft down", "ControlLeft up"]);
  });

  it("releaseAll lets go of everything, locked or not", () => {
    mods.toggle("ControlLeft");
    mods.toggle("ShiftLeft");
    mods.toggle("ShiftLeft");

    mods.releaseAll();

    expect(mods.anyHeld).toBe(false);
    expect(sent.filter((s) => s.endsWith("up")).sort()).toEqual(["ControlLeft up", "ShiftLeft up"]);
  });

  it("releaseAll with nothing held sends nothing", () => {
    mods.releaseAll();

    expect(sent).toEqual([]);
    expect(changes).toBe(0);
  });

  it("forget clears state without sending (the PC already released on disconnect)", () => {
    mods.toggle("MetaLeft");
    sent.length = 0;

    mods.forget();

    expect(mods.anyHeld).toBe(false);
    expect(sent).toEqual([]);
  });

  it("notifies on every visible change", () => {
    mods.toggle("ControlLeft");
    mods.toggle("ControlLeft");
    mods.toggle("ControlLeft");

    expect(changes).toBe(3);
  });
});

describe("charToCode", () => {
  it("maps letters, digits and space", () => {
    expect(charToCode("c")).toBe("KeyC");
    expect(charToCode("C")).toBe("KeyC");
    expect(charToCode("7")).toBe("Digit7");
    expect(charToCode(" ")).toBe("Space");
  });

  it("returns null for anything else", () => {
    expect(charToCode("é")).toBeNull();
    expect(charToCode("!")).toBeNull();
    expect(charToCode("ab")).toBeNull();
    expect(charToCode("")).toBeNull();
  });
});
