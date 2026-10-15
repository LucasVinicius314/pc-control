import { describe, expect, it } from "vitest";
import { ApiError } from "./api";
import { badgeText, changedMessage, failureMessage } from "./dpi";

describe("badgeText", () => {
  it("shows the scale", () => {
    expect(badgeText({ available: true, percent: 200 })).toBe("200%");
    expect(badgeText({ available: true, percent: 100 })).toBe("100%");
  });

  it("shows a dash when the toggle is off or the scale is unknown", () => {
    expect(badgeText({ available: false, percent: 100 })).toBe("–");
    expect(badgeText({ available: true, percent: null })).toBe("–");
    expect(badgeText(null)).toBe("–");
  });
});

describe("changedMessage", () => {
  it("says the new scale and passes on the PC's note about blurry apps", () => {
    expect(changedMessage({ percent: 200, note: "Some apps stay blurry until they are restarted." })).toBe(
      "Display scale is now 200%. Some apps stay blurry until they are restarted.",
    );
  });
});

describe("failureMessage", () => {
  it("prefers the PC's own explanation", () => {
    const error = new ApiError(422, "scale_not_supported", undefined, "This monitor does not allow 200% (its largest is 175%).");

    expect(failureMessage(error)).toBe("This monitor does not allow 200% (its largest is 175%).");
  });

  it("has a sentence for each known problem when the PC gave none", () => {
    expect(failureMessage(new ApiError(409, "busy"))).toContain("already in progress");
    expect(failureMessage(new ApiError(503, "dpi_unavailable"))).toContain("turned off");
    expect(failureMessage(new ApiError(502, "dpi_failed"))).toContain("did not change");
    expect(failureMessage(new ApiError(422, "scale_not_supported"))).toContain("did not change");
  });

  it("says when the phone is no longer paired", () => {
    expect(failureMessage(new ApiError(401, "unauthorized"))).toContain("no longer paired");
  });

  it("blames the network for anything it does not recognise", () => {
    expect(failureMessage(new TypeError("Failed to fetch"))).toContain("Wi-Fi");
    expect(failureMessage(new ApiError(500, "http_500"))).toContain("Wi-Fi");
  });
});
