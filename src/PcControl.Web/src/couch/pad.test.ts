import { describe, expect, it } from "vitest";
import { actionForKey, MIN_STEP_MS, PadReader, StepLimiter, REPEAT_DELAY_MS, REPEAT_INTERVAL_MS, type PadAction, type PadSnapshot } from "./pad";

function snapshot(pressed: number[] = [], axes: number[] = [0, 0, 0, 0]): PadSnapshot {
  const buttons = Array.from({ length: 17 }, (_, i) => pressed.includes(i));
  return { buttons, axes };
}

function reader() {
  const actions: PadAction[] = [];
  return { actions, pad: new PadReader((a) => actions.push(a)) };
}

describe("buttons", () => {
  it("fire once when pressed, not while held", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([0]), 0);
    pad.update(snapshot([0]), 16);
    pad.update(snapshot([0]), 500);

    expect(actions).toEqual(["accept"]);
  });

  it("fire again after being released", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([1]), 0);
    pad.update(snapshot([]), 16);
    pad.update(snapshot([1]), 32);

    expect(actions).toEqual(["back", "back"]);
  });

  it("map the Xbox layout", () => {
    const { actions, pad } = reader();

    for (const [button, t] of [[0, 0], [1, 100], [2, 200], [3, 300], [4, 400], [5, 500], [9, 600], [8, 700]] as const) {
      pad.update(snapshot([button]), t);
      pad.update(snapshot([]), t + 10);
    }

    expect(actions).toEqual(["accept", "back", "x", "y", "prev", "next", "menu", "menu"]);
  });
});

describe("directions", () => {
  it("step once, wait, then repeat steadily while held", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([15]), 0);
    pad.update(snapshot([15]), REPEAT_DELAY_MS - 1);
    expect(actions).toEqual(["right"]);

    pad.update(snapshot([15]), REPEAT_DELAY_MS);
    pad.update(snapshot([15]), REPEAT_DELAY_MS + REPEAT_INTERVAL_MS - 1);
    pad.update(snapshot([15]), REPEAT_DELAY_MS + REPEAT_INTERVAL_MS);

    expect(actions).toEqual(["right", "right", "right"]);
  });

  it("stop repeating when released and start over on the next press", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([12]), 0);
    pad.update(snapshot([]), 100);
    pad.update(snapshot([12]), 200);

    expect(actions).toEqual(["up", "up"]);
  });

  it("change direction at once without waiting", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([14]), 0);
    pad.update(snapshot([13]), 50);

    expect(actions).toEqual(["left", "down"]);
  });

  it("come from the left stick too, along its stronger axis", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([], [0.9, 0.2, 0, 0]), 0);
    pad.update(snapshot([], [0, 0, 0, 0]), 50);
    pad.update(snapshot([], [0.1, -0.8, 0, 0]), 100);

    expect(actions).toEqual(["right", "up"]);
  });

  it("ignore a stick that is barely pushed", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([], [0.3, 0.2, 0, 0]), 0);
    pad.update(snapshot([], [-0.4, 0, 0, 0]), 16);

    expect(actions).toEqual([]);
  });

  it("do not flicker when a stick hovers near the threshold", () => {
    const { actions, pad } = reader();

    pad.update(snapshot([], [0.6, 0, 0, 0]), 0);
    pad.update(snapshot([], [0.5, 0, 0, 0]), 16); // still counted: it has not come back far enough
    pad.update(snapshot([], [0.6, 0, 0, 0]), 32);

    expect(actions).toEqual(["right"]);
  });
});

describe("no controller", () => {
  it("does nothing, and forgets held input", () => {
    const { actions, pad } = reader();
    pad.update(snapshot([0, 15]), 0);

    pad.update(null, 16);
    pad.update(snapshot([0, 15]), 32);

    expect(actions).toEqual(["accept", "right", "accept", "right"]);
  });
});

describe("actionForKey", () => {
  it("maps the keyboard to the same actions", () => {
    expect(actionForKey("ArrowLeft")).toBe("left");
    expect(actionForKey("Enter")).toBe("accept");
    expect(actionForKey("Escape")).toBe("back");
    expect(actionForKey("x")).toBe("x");
    expect(actionForKey("q")).toBe("prev");
    expect(actionForKey("e")).toBe("next");
    expect(actionForKey("F5")).toBeNull();
  });
});

describe("StepLimiter", () => {
  it("lets one step through and drops the same direction arriving again at once", () => {
    const limiter = new StepLimiter();

    expect(limiter.allow("right", 0)).toBe(true);
    expect(limiter.allow("right", 5)).toBe(false);
    expect(limiter.allow("right", MIN_STEP_MS - 1)).toBe(false);
  });

  it("lets the next step through after the window", () => {
    const limiter = new StepLimiter();
    limiter.allow("right", 0);

    expect(limiter.allow("right", MIN_STEP_MS)).toBe(true);
  });

  it("never drops a different direction", () => {
    const limiter = new StepLimiter();
    limiter.allow("right", 0);

    expect(limiter.allow("down", 1)).toBe(true);
  });

  it("still lets a held direction repeat at the normal rate", () => {
    const limiter = new StepLimiter();
    expect(limiter.allow("left", 0)).toBe(true);

    expect(limiter.allow("left", 110)).toBe(true);
    expect(limiter.allow("left", 220)).toBe(true);
  });
});
