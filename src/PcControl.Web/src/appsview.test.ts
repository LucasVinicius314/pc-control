import { describe, expect, it } from "vitest";
import { ApiError, type AppItem } from "./api";
import { buildSections, launchFailureMessage, powerToysMessage, RECENT_SHOWN } from "./appsview";

const app = (id: string, name: string, favorite = false): AppItem => ({ id, name, favorite });

const apps = [app("a", "Calculator"), app("b", "Chrome", true), app("c", "Edge"), app("d", "Notepad", true), app("e", "Paint")];

describe("buildSections", () => {
  it("shows favorites, recent and all, in that order", () => {
    const sections = buildSections(apps, ["e", "a"], "");

    expect(sections.map((s) => s.title)).toEqual(["Favorites", "Recent", "All apps"]);
    expect(sections[0]?.apps.map((a) => a.name)).toEqual(["Chrome", "Notepad"]);
    expect(sections[1]?.apps.map((a) => a.name)).toEqual(["Paint", "Calculator"]);
    expect(sections[2]?.apps).toHaveLength(5);
  });

  it("leaves out sections that would be empty", () => {
    expect(buildSections([app("a", "One")], [], "").map((s) => s.title)).toEqual(["All apps"]);
    expect(buildSections([app("a", "One", true)], [], "").map((s) => s.title)).toEqual(["Favorites", "All apps"]);
  });

  it("lists an app that is both a favorite and recent in both sections", () => {
    const sections = buildSections(apps, ["b"], "");

    expect(sections[0]?.apps.map((a) => a.id)).toContain("b");
    expect(sections[1]?.apps.map((a) => a.id)).toEqual(["b"]);
  });

  it("ignores recent ids that are not in the list any more", () => {
    const sections = buildSections(apps, ["gone", "a"], "");

    expect(sections.find((s) => s.title === "Recent")?.apps.map((a) => a.id)).toEqual(["a"]);
  });

  it("shows only the latest few recent apps", () => {
    const many = Array.from({ length: 20 }, (_, i) => app(`id${i}`, `App ${i}`));

    const recent = buildSections(many, many.map((a) => a.id), "").find((s) => s.title === "Recent");

    expect(recent?.apps).toHaveLength(RECENT_SHOWN);
    expect(recent?.apps[0]?.id).toBe("id0");
  });

  it("replaces the sections with one ranked list while searching", () => {
    const sections = buildSections(apps, ["a"], "pa");

    expect(sections.map((s) => s.title)).toEqual(["Results"]);
    expect(sections[0]?.apps.map((a) => a.name)).toEqual(["Paint", "Notepad"]);
  });

  it("returns no sections when nothing matches, so the page can say so", () => {
    expect(buildSections(apps, [], "zzzz")).toEqual([]);
  });

  it("treats a blank search as no search", () => {
    expect(buildSections(apps, [], "   ").map((s) => s.title)).toContain("All apps");
  });

  it("finds an app whatever its accents", () => {
    const sections = buildSections([app("x", "Édge")], [], "edge");

    expect(sections[0]?.apps.map((a) => a.name)).toEqual(["Édge"]);
  });

  it("does not change the list it was given", () => {
    const copy = apps.map((a) => ({ ...a }));

    buildSections(apps, ["a"], "pa");

    expect(apps).toEqual(copy);
  });
});

describe("launchFailureMessage", () => {
  it("explains the kill switch", () => {
    expect(launchFailureMessage(new ApiError(409, "paused"))).toContain("paused");
  });

  it("says when an app disappeared", () => {
    expect(launchFailureMessage(new ApiError(404, "unknown_app"))).toContain("no longer");
  });

  it("uses the PC's sentence when Windows could not start the app", () => {
    expect(launchFailureMessage(new ApiError(502, "launch_failed", undefined, "Windows could not start Chrome."))).toBe(
      "Windows could not start Chrome.",
    );
    expect(launchFailureMessage(new ApiError(502, "launch_failed"), "Chrome")).toBe("Windows could not start Chrome.");
  });

  it("says when the phone is not paired any more", () => {
    expect(launchFailureMessage(new ApiError(401, "unauthorized"))).toContain("no longer paired");
  });

  it("blames the network for anything else", () => {
    expect(launchFailureMessage(new TypeError("Failed to fetch"))).toContain("Wi-Fi");
  });
});

describe("powerToysMessage", () => {
  it("names the shortcut", () => {
    expect(powerToysMessage("Shift+Win+Space", true)).toContain("Shift+Win+Space");
  });

  it("points at PowerToys' settings when the shortcut was only a guess", () => {
    expect(powerToysMessage("Alt+Space", false)).toContain("settings");
  });
});
