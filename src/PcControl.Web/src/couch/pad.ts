// Turns a game controller (the browser's Gamepad API, "standard" layout: an Xbox controller) and the
// keyboard into the few actions the controller UI understands. The state machine is pure and takes the
// time as an argument, so repeat behaviour is tested without a controller.

export type PadAction = "up" | "down" | "left" | "right" | "accept" | "back" | "x" | "y" | "prev" | "next" | "menu";

export interface PadSnapshot {
  buttons: readonly boolean[];
  axes: readonly number[];
}

// Indexes of the "standard" mapping.
const BTN = { a: 0, b: 1, x: 2, y: 3, lb: 4, rb: 5, back: 8, start: 9, up: 12, down: 13, left: 14, right: 15 } as const;

/** How far a stick has to be pushed before it counts, and how far back it has to return to be released. */
const STICK_ON = 0.55;
const STICK_OFF = 0.35;

/** A held direction repeats like a held key: a pause, then steady steps. */
export const REPEAT_DELAY_MS = 380;
export const REPEAT_INTERVAL_MS = 110;

const DIRECTIONS: readonly PadAction[] = ["up", "down", "left", "right"];

/** The keyboard keys that do the same things, so the UI can be tried without a controller. */
export function actionForKey(key: string): PadAction | null {
  switch (key) {
    case "ArrowUp":
      return "up";
    case "ArrowDown":
      return "down";
    case "ArrowLeft":
      return "left";
    case "ArrowRight":
      return "right";
    case "Enter":
    case " ":
      return "accept";
    case "Escape":
    case "Backspace":
      return "back";
    case "x":
    case "X":
      return "x";
    case "y":
    case "Y":
      return "y";
    case "PageUp":
    case "q":
    case "Q":
      return "prev";
    case "PageDown":
    case "e":
    case "E":
      return "next";
    case "Tab":
      return "menu";
    default:
      return null;
  }
}

/**
 * Lets one step through per physical press. A bouncing D-pad, or the same press arriving twice (from
 * the controller and again as an arrow key, which Steam's controller settings can do), would
 * otherwise move the focus several tiles at once. Deliberate repeats while a direction is held are
 * slower than this window, so they still get through.
 */
export const MIN_STEP_MS = 90;

export class StepLimiter {
  private last: { action: PadAction; at: number } | null = null;

  allow(action: PadAction, now: number): boolean {
    if (this.last !== null && this.last.action === action && now - this.last.at < MIN_STEP_MS) return false;
    this.last = { action, at: now };
    return true;
  }
}

export class PadReader {
  private pressed = new Set<string>();
  private heldDirection: PadAction | null = null;
  private nextRepeat = 0;

  constructor(private readonly onAction: (action: PadAction) => void) {}

  /** Call every frame with the controller's state (or null when none is connected) and the time in ms. */
  update(snapshot: PadSnapshot | null, now: number): void {
    if (snapshot === null) {
      this.pressed.clear();
      this.heldDirection = null;
      return;
    }

    const b = snapshot.buttons;
    this.edge("accept", b[BTN.a] === true);
    this.edge("back", b[BTN.b] === true);
    this.edge("x", b[BTN.x] === true);
    this.edge("y", b[BTN.y] === true);
    this.edge("prev", b[BTN.lb] === true);
    this.edge("next", b[BTN.rb] === true);
    this.edge("menu", b[BTN.start] === true || b[BTN.back] === true);

    this.direction(this.currentDirection(snapshot), now);
  }

  private edge(action: PadAction, down: boolean): void {
    const was = this.pressed.has(action);
    if (down && !was) {
      this.pressed.add(action);
      this.onAction(action);
    } else if (!down && was) {
      this.pressed.delete(action);
    }
  }

  private currentDirection(snapshot: PadSnapshot): PadAction | null {
    const b = snapshot.buttons;
    if (b[BTN.up]) return "up";
    if (b[BTN.down]) return "down";
    if (b[BTN.left]) return "left";
    if (b[BTN.right]) return "right";

    const x = snapshot.axes[0] ?? 0;
    const y = snapshot.axes[1] ?? 0;
    // A stick that is already counted stays counted until it comes back past the lower threshold.
    const threshold = this.heldDirection !== null ? STICK_OFF : STICK_ON;
    if (Math.max(Math.abs(x), Math.abs(y)) < threshold) return null;
    return Math.abs(x) > Math.abs(y) ? (x > 0 ? "right" : "left") : y > 0 ? "down" : "up";
  }

  private direction(direction: PadAction | null, now: number): void {
    if (direction === null || !DIRECTIONS.includes(direction)) {
      this.heldDirection = null;
      return;
    }
    if (direction !== this.heldDirection) {
      this.heldDirection = direction;
      this.nextRepeat = now + REPEAT_DELAY_MS;
      this.onAction(direction);
      return;
    }
    if (now >= this.nextRepeat) {
      this.nextRepeat = now + REPEAT_INTERVAL_MS;
      this.onAction(direction);
    }
  }
}
