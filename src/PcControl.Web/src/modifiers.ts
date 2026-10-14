// Ctrl, Alt, Shift and Win buttons. Tap once and the next key or click is sent with it ("once");
// tap again to keep it held ("locked"); tap a third time to let go.

export type ModifierState = "off" | "once" | "locked";

export const MODIFIER_CODES = {
  ctrl: "ControlLeft",
  alt: "AltLeft",
  shift: "ShiftLeft",
  win: "MetaLeft",
} as const;

export class Modifiers {
  private readonly states = new Map<string, ModifierState>();

  /** @param send Presses (down=true) or releases (down=false) a modifier key on the PC. */
  constructor(
    private readonly send: (code: string, down: boolean) => void,
    private readonly onChange: () => void = () => {},
  ) {}

  state(code: string): ModifierState {
    return this.states.get(code) ?? "off";
  }

  get anyHeld(): boolean {
    for (const state of this.states.values()) if (state !== "off") return true;
    return false;
  }

  toggle(code: string): void {
    switch (this.state(code)) {
      case "off":
        this.states.set(code, "once");
        this.send(code, true);
        break;
      case "once":
        this.states.set(code, "locked");
        break;
      case "locked":
        this.states.delete(code);
        this.send(code, false);
        break;
    }
    this.onChange();
  }

  /** Call after a key or click was sent: modifiers that were only "once" are released now. */
  consumeOnce(): void {
    let changed = false;
    for (const [code, state] of [...this.states]) {
      if (state === "once") {
        this.states.delete(code);
        this.send(code, false);
        changed = true;
      }
    }
    if (changed) this.onChange();
  }

  releaseAll(): void {
    if (this.states.size === 0) return;
    for (const code of this.states.keys()) this.send(code, false);
    this.states.clear();
    this.onChange();
  }

  /** The connection was lost; the PC already released everything, so just forget. */
  forget(): void {
    if (this.states.size === 0) return;
    this.states.clear();
    this.onChange();
  }
}

/** The key code for a single typed character when a shortcut modifier is held, or null. */
export function charToCode(char: string): string | null {
  if (/^[a-z]$/i.test(char)) return "Key" + char.toUpperCase();
  if (/^[0-9]$/.test(char)) return "Digit" + char;
  if (char === " ") return "Space";
  return null;
}
