import { describe, expect, it } from "vitest";
import { ICONS, type IconName } from "./icons";

const TOOLBAR: IconName[] = ["keyboard", "leftClick", "rightClick", "keys", "apps", "search", "view", "dpi", "media", "power", "settings"];
const PANEL: IconName[] = ["lock", "sleep", "signOut", "restart", "closeX", "force", "link"];
const ALL = [...TOOLBAR, ...PANEL];

describe("toolbar icons", () => {
  it("has a drawing for every toolbar button", () => {
    for (const name of ALL) expect(ICONS[name]?.length).toBeGreaterThan(0);
  });

  it("has nothing besides the toolbar and panel buttons", () => {
    expect(Object.keys(ICONS).sort()).toEqual([...ALL].sort());
  });

  it("only uses shapes with their required attributes", () => {
    const required: Record<string, string[]> = {
      path: ["d"],
      rect: ["x", "y", "width", "height"],
      circle: ["cx", "cy", "r"],
    };

    for (const shapes of Object.values(ICONS)) {
      for (const shape of shapes) {
        for (const attr of required[shape.tag] ?? []) expect(shape.attrs[attr], `${shape.tag} needs ${attr}`).toBeDefined();
      }
    }
  });

  it("keeps every number inside the 24 x 24 drawing grid", () => {
    for (const shapes of Object.values(ICONS)) {
      for (const shape of shapes) {
        if (shape.tag === "path") continue;
        const numbers = Object.entries(shape.attrs)
          .filter(([key]) => ["x", "y", "cx", "cy", "width", "height"].includes(key))
          .map(([, value]) => Number(value));
        for (const n of numbers) expect(n).toBeGreaterThanOrEqual(0);
        for (const n of numbers) expect(n).toBeLessThanOrEqual(24);
      }
    }
  });

  it("tells the two mouse buttons apart by which half is filled", () => {
    const filled = (name: IconName) => ICONS[name].filter((s) => s.filled).map((s) => s.attrs["d"]);

    expect(filled("leftClick")).toHaveLength(1);
    expect(filled("rightClick")).toHaveLength(1);
    expect(filled("leftClick")).not.toEqual(filled("rightClick"));
  });
});
