import { describe, expect, it } from "vitest";
import { barsFor, describeController } from "./controllers";

describe("barsFor", () => {
  it("lights none when empty and all three when full", () => {
    expect(barsFor("empty")).toBe(0);
    expect(barsFor("low")).toBe(1);
    expect(barsFor("medium")).toBe(2);
    expect(barsFor("full")).toBe(3);
    expect(barsFor(null)).toBe(0);
    expect(barsFor(undefined)).toBe(0);
  });
});

describe("describeController", () => {
  it("says what it runs on", () => {
    expect(describeController({ slot: 0, power: "wired" }, 1)).toBe("Controller 1: wired");
    expect(describeController({ slot: 2, power: "battery", level: "full" }, 2)).toBe("Controller 2: battery full");
    expect(describeController({ slot: 0, power: "battery", level: "medium" }, 1)).toBe("Controller 1: battery half");
    expect(describeController({ slot: 0, power: "unknown" }, 1)).toBe("Controller 1: connected");
    expect(describeController({ slot: 0, power: "battery", level: "empty", percent: 10 }, 1)).toBe("Controller 1: battery 10%");
    expect(describeController({ slot: 0, power: "battery", level: "low", percent: 24, charging: true }, 1)).toBe("Controller 1: battery 24%, charging");
  });
});
