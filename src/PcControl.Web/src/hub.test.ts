import { describe, expect, it } from "vitest";
import { pickTab } from "./hub";

describe("pickTab", () => {
  it("keeps the remembered tab while it exists", () => {
    expect(pickTab(["apps", "running", "link"], "running")).toBe("running");
  });

  it("falls back to the first tab when the remembered one is gone or unknown", () => {
    expect(pickTab(["running", "link"], "apps")).toBe("running");
    expect(pickTab(["apps", "link"], null)).toBe("apps");
  });

  it("has nothing to show without tabs", () => {
    expect(pickTab([], "apps")).toBeNull();
  });
});
