// Trackpad gesture recognition. The rules and numbers are specified in docs/protocol/gestures.md.
// This file knows nothing about the DOM or the network, so it can be tested without a phone.

export const TOUCH_SLOP = 6;
export const TAP_MAX_MS = 250;
export const HOLD_MS = 400;
export const TWO_FINGER_TAP_MAX_MS = 300;
export const TWO_FINGER_SLOP = 10;
export const MODE_DECISION_PX = 12;
export const SCROLL_UNITS_PER_PX = 2.4;
export const PINCH_STEP_PX = 40;

export type Button = "left" | "right";

export interface GestureOutput {
  move(dx: number, dy: number): void;
  /** Wheel units (120 is one notch). Positive vertical scrolls up, positive horizontal scrolls right. */
  scroll(vertical: number, horizontal: number): void;
  click(button: Button): void;
  buttonDown(button: Button): void;
  buttonUp(button: Button): void;
  /** Positive zooms in. */
  zoom(steps: number): void;
  haptic(): void;
}

export interface Pointer {
  id: number;
  x: number;
  y: number;
  /** Milliseconds, from any monotonic clock (event.timeStamp works). */
  t: number;
}

export interface GestureOptions {
  naturalScroll: boolean;
}

type Mode = "undecided" | "scroll" | "pinch";

type State =
  | { kind: "idle" }
  | { kind: "pending"; id: number; startX: number; startY: number; lastX: number; lastY: number; t0: number }
  | { kind: "moving"; id: number; lastX: number; lastY: number }
  | { kind: "dragging"; id: number; lastX: number; lastY: number }
  | {
      kind: "two";
      ids: [number, number];
      t0: number;
      mode: Mode;
      lastCx: number;
      lastCy: number;
      lastDist: number;
      totalCx: number;
      totalCy: number;
      totalDist: number;
      wheelV: number;
      wheelH: number;
      pinchAcc: number;
    }
  | { kind: "waitRelease" };

export class GestureRecognizer {
  private state: State = { kind: "idle" };
  private readonly down = new Map<number, { x: number; y: number }>();

  constructor(
    private readonly out: GestureOutput,
    public options: GestureOptions = { naturalScroll: true },
  ) {}

  /** True while a finger is down, so the caller knows to keep calling tick(). */
  get active(): boolean {
    return this.down.size > 0;
  }

  pointerDown(p: Pointer): void {
    if (this.down.size >= 2 && !this.down.has(p.id)) return; // a third finger is ignored
    this.down.set(p.id, { x: p.x, y: p.y });

    const s = this.state;
    switch (s.kind) {
      case "idle":
        this.state = { kind: "pending", id: p.id, startX: p.x, startY: p.y, lastX: p.x, lastY: p.y, t0: p.t };
        break;
      case "pending":
      case "moving":
        this.startTwo(s.id, p);
        break;
      default:
        break; // dragging or already two-finger: ignore the extra finger
    }
  }

  pointerMove(p: Pointer): void {
    const known = this.down.get(p.id);
    if (!known) return;
    known.x = p.x;
    known.y = p.y;

    const s = this.state;
    switch (s.kind) {
      case "pending": {
        if (p.id !== s.id) return;
        if (Math.hypot(p.x - s.startX, p.y - s.startY) > TOUCH_SLOP) {
          this.out.move(p.x - s.startX, p.y - s.startY);
          this.state = { kind: "moving", id: s.id, lastX: p.x, lastY: p.y };
        }
        break;
      }
      case "moving":
      case "dragging": {
        if (p.id !== s.id) return;
        this.out.move(p.x - s.lastX, p.y - s.lastY);
        s.lastX = p.x;
        s.lastY = p.y;
        break;
      }
      case "two":
        this.moveTwo(s);
        break;
      default:
        break;
    }
  }

  pointerUp(p: Pointer): void {
    if (!this.down.has(p.id)) return;
    const s = this.state;

    switch (s.kind) {
      case "pending":
        if (p.id === s.id && p.t - s.t0 <= TAP_MAX_MS) this.out.click("left");
        this.state = { kind: "idle" };
        break;
      case "moving":
        this.state = { kind: "idle" };
        break;
      case "dragging":
        if (p.id === s.id) {
          this.out.buttonUp("left");
          this.state = { kind: "idle" };
        }
        break;
      case "two":
        if (
          s.mode === "undecided" &&
          p.t - s.t0 <= TWO_FINGER_TAP_MAX_MS &&
          Math.hypot(s.totalCx, s.totalCy) <= TWO_FINGER_SLOP &&
          Math.abs(s.totalDist) <= TWO_FINGER_SLOP
        ) {
          this.out.click("right");
        }
        this.state = { kind: "waitRelease" };
        break;
      default:
        break;
    }

    this.down.delete(p.id);
    if (this.down.size === 0) this.state = { kind: "idle" };
  }

  /** The browser took the touch away (for example a system gesture). Never leave a button held. */
  pointerCancel(): void {
    if (this.state.kind === "dragging") this.out.buttonUp("left");
    this.down.clear();
    this.state = { kind: "idle" };
  }

  /** Call regularly (every ~50 ms) while a finger is down; starts the press-and-hold drag. */
  tick(now: number): void {
    const s = this.state;
    if (s.kind === "pending" && now - s.t0 >= HOLD_MS) {
      this.out.buttonDown("left");
      this.out.haptic();
      this.state = { kind: "dragging", id: s.id, lastX: s.lastX, lastY: s.lastY };
    }
  }

  private startTwo(firstId: number, second: Pointer): void {
    const first = this.down.get(firstId);
    if (!first) return;
    const cx = (first.x + second.x) / 2;
    const cy = (first.y + second.y) / 2;
    this.state = {
      kind: "two",
      ids: [firstId, second.id],
      t0: second.t,
      mode: "undecided",
      lastCx: cx,
      lastCy: cy,
      lastDist: Math.hypot(first.x - second.x, first.y - second.y),
      totalCx: 0,
      totalCy: 0,
      totalDist: 0,
      wheelV: 0,
      wheelH: 0,
      pinchAcc: 0,
    };
  }

  private moveTwo(s: Extract<State, { kind: "two" }>): void {
    const a = this.down.get(s.ids[0]);
    const b = this.down.get(s.ids[1]);
    if (!a || !b) return;

    const cx = (a.x + b.x) / 2;
    const cy = (a.y + b.y) / 2;
    const dist = Math.hypot(a.x - b.x, a.y - b.y);
    const dCx = cx - s.lastCx;
    const dCy = cy - s.lastCy;
    const dDist = dist - s.lastDist;
    s.lastCx = cx;
    s.lastCy = cy;
    s.lastDist = dist;
    s.totalCx += dCx;
    s.totalCy += dCy;
    s.totalDist += dDist;

    if (s.mode === "undecided") {
      const travelled = Math.hypot(s.totalCx, s.totalCy);
      const stretched = Math.abs(s.totalDist);
      if (Math.max(travelled, stretched) < MODE_DECISION_PX) return;
      s.mode = stretched > travelled ? "pinch" : "scroll";
      // Count everything since the fingers landed, so the first part of the gesture is not lost.
      if (s.mode === "scroll") this.addScroll(s, s.totalCx, s.totalCy);
      else this.addPinch(s, s.totalDist);
      return;
    }

    if (s.mode === "scroll") this.addScroll(s, dCx, dCy);
    else this.addPinch(s, dDist);
  }

  private addScroll(s: Extract<State, { kind: "two" }>, dx: number, dy: number): void {
    const sign = this.options.naturalScroll ? 1 : -1;
    s.wheelV += dy * SCROLL_UNITS_PER_PX * sign;
    s.wheelH += -dx * SCROLL_UNITS_PER_PX * sign;

    const v = Math.trunc(s.wheelV);
    const h = Math.trunc(s.wheelH);
    if (v !== 0 || h !== 0) {
      s.wheelV -= v;
      s.wheelH -= h;
      this.out.scroll(v, h);
    }
  }

  private addPinch(s: Extract<State, { kind: "two" }>, dDist: number): void {
    s.pinchAcc += dDist;
    const steps = Math.trunc(s.pinchAcc / PINCH_STEP_PX);
    if (steps !== 0) {
      s.pinchAcc -= steps * PINCH_STEP_PX;
      this.out.zoom(steps);
    }
  }
}
