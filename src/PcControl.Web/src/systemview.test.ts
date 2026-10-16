import { describe, expect, it } from "vitest";
import { ApiError } from "./api";
import { ConfirmGate, countdownText, needsConfirmation, powerDone, powerMessage, processMessage, urlMessage } from "./systemview";

describe("needsConfirmation", () => {
  it("is needed for the actions that end the session only", () => {
    expect(needsConfirmation("lock")).toBe(false);
    expect(needsConfirmation("sleep")).toBe(false);
    expect(needsConfirmation("restart")).toBe(true);
    expect(needsConfirmation("shutdown")).toBe(true);
    expect(needsConfirmation("signOut")).toBe(true);
  });
});

describe("ConfirmGate", () => {
  it("arms on the first tap and confirms on the second", () => {
    let t = 0;
    const gate = new ConfirmGate(4000, () => t);

    expect(gate.tap("shutdown")).toBe(false);
    expect(gate.isArmed("shutdown")).toBe(true);
    t = 1000;
    expect(gate.tap("shutdown")).toBe(true);
    expect(gate.isArmed("shutdown")).toBe(false);
  });

  it("lets the arming run out", () => {
    let t = 0;
    const gate = new ConfirmGate(4000, () => t);
    gate.tap("restart");
    t = 4001;

    expect(gate.isArmed("restart")).toBe(false);
    expect(gate.tap("restart")).toBe(false); // that tap arms again
  });

  it("does not let a tap on another button confirm", () => {
    const gate = new ConfirmGate(4000, () => 0);
    gate.tap("restart");

    expect(gate.tap("shutdown")).toBe(false);
    expect(gate.isArmed("restart")).toBe(false);
  });

  it("can be reset", () => {
    const gate = new ConfirmGate(4000, () => 0);
    gate.tap("signOut");
    gate.reset();

    expect(gate.tap("signOut")).toBe(false);
  });
});

describe("messages", () => {
  it("explain the usual failures", () => {
    expect(urlMessage(new ApiError(400, "invalid_url"))).toMatch(/http/);
    expect(urlMessage(new ApiError(409, "paused"))).toMatch(/paused/);
    expect(urlMessage(new ApiError(403, "feature_disabled"))).toMatch(/turned off/);
    expect(powerMessage(new ApiError(502, "power_failed", undefined, "No."))).toBe("No.");
    expect(processMessage(new ApiError(404, "process_gone"))).toMatch(/already closed/);
    expect(processMessage(new ApiError(502, "close_failed"))).toMatch(/Force close/);
  });

  it("blame the network for anything that is not an API error", () => {
    expect(urlMessage(new TypeError("fetch failed"))).toMatch(/Wi-Fi/);
    expect(powerMessage(new TypeError("fetch failed"))).toMatch(/Wi-Fi/);
  });

  it("tells a lost pairing apart", () => {
    expect(powerMessage(new ApiError(401, "http_401"))).toMatch(/paired/);
  });
});

describe("countdown", () => {
  it("rounds up and never goes negative", () => {
    expect(countdownText("shutdown", 14.2)).toBe("Shutting down in 15 s");
    expect(countdownText("restart", -3)).toBe("Restarting in 0 s");
  });

  it("describes what happened", () => {
    expect(powerDone("lock", 0)).toBe("PC locked.");
    expect(powerDone("restart", 15)).toMatch(/Restarting in 15 s/);
  });
});
