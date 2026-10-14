import { beforeEach, describe, expect, it } from "vitest";
import {
  GestureRecognizer,
  HOLD_MS,
  MODE_DECISION_PX,
  PINCH_STEP_PX,
  SCROLL_UNITS_PER_PX,
  TAP_MAX_MS,
  TOUCH_SLOP,
  TWO_FINGER_TAP_MAX_MS,
  type GestureOutput,
} from "./gestures";

class Recorder implements GestureOutput {
  events: string[] = [];
  moves: [number, number][] = [];
  scrolls: [number, number][] = [];
  zooms: number[] = [];

  move(dx: number, dy: number) {
    this.moves.push([dx, dy]);
    this.events.push(`move ${dx},${dy}`);
  }
  scroll(v: number, h: number) {
    this.scrolls.push([v, h]);
    this.events.push(`scroll ${v},${h}`);
  }
  click(button: string) {
    this.events.push(`click ${button}`);
  }
  buttonDown(button: string) {
    this.events.push(`down ${button}`);
  }
  buttonUp(button: string) {
    this.events.push(`up ${button}`);
  }
  zoom(steps: number) {
    this.zooms.push(steps);
    this.events.push(`zoom ${steps}`);
  }
  haptic() {
    this.events.push("haptic");
  }

  get totalScroll(): [number, number] {
    return this.scrolls.reduce<[number, number]>((a, s) => [a[0] + s[0], a[1] + s[1]], [0, 0]);
  }
}

const p = (id: number, x: number, y: number, t: number) => ({ id, x, y, t });

let out: Recorder;
let g: GestureRecognizer;

beforeEach(() => {
  out = new Recorder();
  g = new GestureRecognizer(out);
});

describe("one finger", () => {
  it("a quick touch without moving is a left click", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerUp(p(1, 100, 100, TAP_MAX_MS - 10));

    expect(out.events).toEqual(["click left"]);
  });

  it("a touch that is too slow is not a click", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerUp(p(1, 100, 100, TAP_MAX_MS + 10));

    expect(out.events).toEqual([]);
  });

  it("tiny jitter inside the slop still counts as a tap", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerMove(p(1, 100 + TOUCH_SLOP - 1, 100, 20));
    g.pointerUp(p(1, 100 + TOUCH_SLOP - 1, 100, 60));

    expect(out.events).toEqual(["click left"]);
  });

  it("moving past the slop sends the whole movement so far, then follows the finger", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerMove(p(1, 110, 100, 10));
    g.pointerMove(p(1, 115, 103, 20));

    expect(out.moves).toEqual([
      [10, 0],
      [5, 3],
    ]);
  });

  it("a moved touch never clicks on release", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerMove(p(1, 150, 100, 10));
    g.pointerUp(p(1, 150, 100, 50));

    expect(out.events).not.toContain("click left");
  });

  it("holding still presses the button, vibrates and release lifts it", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.tick(HOLD_MS - 1);
    expect(out.events).toEqual([]);

    g.tick(HOLD_MS);
    g.pointerUp(p(1, 100, 100, HOLD_MS + 100));

    expect(out.events).toEqual(["down left", "haptic", "up left"]);
  });

  it("moving after the hold drags", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.tick(HOLD_MS);
    g.pointerMove(p(1, 130, 110, HOLD_MS + 20));
    g.pointerUp(p(1, 130, 110, HOLD_MS + 40));

    expect(out.events).toEqual(["down left", "haptic", "move 30,10", "up left"]);
  });

  it("the hold does not trigger once the finger has started moving", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerMove(p(1, 140, 100, 100));
    g.tick(HOLD_MS + 500);

    expect(out.events).toEqual(["move 40,0"]);
  });

  it("cancelling during a drag releases the button", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.tick(HOLD_MS);
    g.pointerCancel();

    expect(out.events).toEqual(["down left", "haptic", "up left"]);
    expect(g.active).toBe(false);
  });

  it("is idle again after lifting", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerUp(p(1, 100, 100, 50));

    expect(g.active).toBe(false);
  });
});

describe("two fingers", () => {
  const land = (t = 0) => {
    g.pointerDown(p(1, 100, 100, t));
    g.pointerDown(p(2, 200, 100, t + 10));
  };

  it("a quick two-finger touch is a right click", () => {
    land();
    g.pointerUp(p(1, 100, 100, TWO_FINGER_TAP_MAX_MS - 20));
    g.pointerUp(p(2, 200, 100, TWO_FINGER_TAP_MAX_MS - 10));

    expect(out.events).toEqual(["click right"]);
  });

  it("a slow two-finger touch is not a click", () => {
    land();
    g.pointerUp(p(1, 100, 100, 10 + TWO_FINGER_TAP_MAX_MS + 50));
    g.pointerUp(p(2, 200, 100, 10 + TWO_FINGER_TAP_MAX_MS + 60));

    expect(out.events).toEqual([]);
  });

  it("the second finger cancels a pending left click", () => {
    land();
    g.pointerUp(p(1, 100, 100, 100));
    g.pointerUp(p(2, 200, 100, 110));

    expect(out.events).toEqual(["click right"]);
  });

  it("the second finger cancels a pending hold", () => {
    land();
    g.tick(HOLD_MS + 100);

    expect(out.events).toEqual([]);
  });

  it("dragging both fingers down scrolls up with natural scrolling", () => {
    land();
    for (let y = 100; y <= 220; y += 10) {
      g.pointerMove(p(1, 100, y, 50));
      g.pointerMove(p(2, 200, y, 50));
    }

    const [v, h] = out.totalScroll;
    expect(v).toBeGreaterThan(0);
    expect(h).toBe(0);
    // 120 px of travel is 120 * 2.4 wheel units, give or take the remainder carried over.
    expect(Math.abs(v - 120 * SCROLL_UNITS_PER_PX)).toBeLessThan(2);
  });

  it("dragging up scrolls the other way, and turning natural scrolling off flips it", () => {
    land();
    for (let y = 100; y >= 0; y -= 10) {
      g.pointerMove(p(1, 100, y, 50));
      g.pointerMove(p(2, 200, y, 50));
    }
    expect(out.totalScroll[0]).toBeLessThan(0);

    g.pointerCancel();
    out = new Recorder();
    g = new GestureRecognizer(out, { naturalScroll: false });
    land();
    for (let y = 100; y <= 200; y += 10) {
      g.pointerMove(p(1, 100, y, 50));
      g.pointerMove(p(2, 200, y, 50));
    }
    expect(out.totalScroll[0]).toBeLessThan(0);
  });

  it("dragging sideways sends a horizontal value with the opposite sign", () => {
    land();
    for (let x = 0; x <= 100; x += 10) {
      g.pointerMove(p(1, 100 + x, 100, 50));
      g.pointerMove(p(2, 200 + x, 100, 50));
    }

    const [v, h] = out.totalScroll;
    expect(v).toBe(0);
    expect(h).toBeLessThan(0);
  });

  it("slow scrolling is not lost to rounding", () => {
    land();
    // Get past the decision threshold first, then crawl 0.1 px at a time.
    g.pointerMove(p(1, 100, 100 + MODE_DECISION_PX + 1, 50));
    g.pointerMove(p(2, 200, 100 + MODE_DECISION_PX + 1, 50));
    out.scrolls.length = 0;

    for (let i = 1; i <= 100; i++) {
      g.pointerMove(p(1, 100, 100 + MODE_DECISION_PX + 1 + i * 0.1, 60));
      g.pointerMove(p(2, 200, 100 + MODE_DECISION_PX + 1 + i * 0.1, 60));
    }

    expect(Math.abs(out.totalScroll[0] - 10 * SCROLL_UNITS_PER_PX)).toBeLessThan(1.5);
  });

  it("spreading the fingers zooms in, one step per PINCH_STEP_PX", () => {
    land();
    // Fingers 100 px apart; move each outward by 40 px (distance +80 -> two steps).
    for (let i = 1; i <= 8; i++) {
      g.pointerMove(p(1, 100 - i * 5, 100, 50));
      g.pointerMove(p(2, 200 + i * 5, 100, 50));
    }

    expect(out.zooms.reduce((a, b) => a + b, 0)).toBe(Math.trunc(80 / PINCH_STEP_PX));
    expect(out.scrolls).toEqual([]);
  });

  it("closing the fingers zooms out", () => {
    land();
    for (let i = 1; i <= 8; i++) {
      g.pointerMove(p(1, 100 + i * 5, 100, 50));
      g.pointerMove(p(2, 200 - i * 5, 100, 50));
    }

    expect(out.zooms.reduce((a, b) => a + b, 0)).toBeLessThan(0);
  });

  it("once decided, the mode does not switch mid-gesture", () => {
    land();
    for (let y = 100; y <= 140; y += 10) {
      g.pointerMove(p(1, 100, y, 50));
      g.pointerMove(p(2, 200, y, 50));
    }
    expect(out.scrolls.length).toBeGreaterThan(0);

    for (let i = 1; i <= 10; i++) {
      g.pointerMove(p(1, 100 - i * 8, 140, 80));
      g.pointerMove(p(2, 200 + i * 8, 140, 80));
    }

    expect(out.zooms).toEqual([]);
  });

  it("a scroll or pinch never ends in a right click", () => {
    land();
    for (let y = 100; y <= 160; y += 10) {
      g.pointerMove(p(1, 100, y, 50));
      g.pointerMove(p(2, 200, y, 50));
    }
    g.pointerUp(p(1, 100, 160, 120));
    g.pointerUp(p(2, 200, 160, 130));

    expect(out.events).not.toContain("click right");
  });

  it("the remaining finger is ignored after one lifts, until all are up", () => {
    land();
    g.pointerMove(p(1, 100, 130, 50));
    g.pointerMove(p(2, 200, 130, 50));
    g.pointerUp(p(1, 100, 130, 60));
    out.events.length = 0;

    g.pointerMove(p(2, 300, 300, 70));
    g.pointerUp(p(2, 300, 300, 80));
    expect(out.events).toEqual([]);

    // And a fresh touch afterwards works normally.
    g.pointerDown(p(3, 50, 50, 500));
    g.pointerUp(p(3, 50, 50, 540));
    expect(out.events).toEqual(["click left"]);
  });

  it("a third finger is ignored", () => {
    land();
    g.pointerDown(p(3, 150, 200, 20));
    g.pointerUp(p(3, 150, 200, 40));
    g.pointerUp(p(1, 100, 100, 100));
    g.pointerUp(p(2, 200, 100, 110));

    expect(out.events).toEqual(["click right"]);
  });

  it("a second finger landing during a drag is ignored and the drag continues", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.tick(HOLD_MS);
    g.pointerDown(p(2, 300, 300, HOLD_MS + 10));
    g.pointerMove(p(1, 120, 100, HOLD_MS + 20));
    g.pointerUp(p(2, 300, 300, HOLD_MS + 30));
    g.pointerUp(p(1, 120, 100, HOLD_MS + 40));

    expect(out.events).toEqual(["down left", "haptic", "move 20,0", "up left"]);
  });

  it("a second finger after the cursor started moving starts a two-finger gesture", () => {
    g.pointerDown(p(1, 100, 100, 0));
    g.pointerMove(p(1, 160, 100, 20));
    g.pointerDown(p(2, 260, 100, 40));
    out.events.length = 0;
    g.pointerUp(p(1, 160, 100, 80));
    g.pointerUp(p(2, 260, 100, 90));

    expect(out.events).toEqual(["click right"]);
  });
});

describe("robustness", () => {
  it("ignores events for fingers it never saw", () => {
    g.pointerMove(p(9, 1, 1, 0));
    g.pointerUp(p(9, 1, 1, 10));

    expect(out.events).toEqual([]);
  });

  it("cancel with nothing down is harmless", () => {
    g.pointerCancel();

    expect(out.events).toEqual([]);
  });
});
