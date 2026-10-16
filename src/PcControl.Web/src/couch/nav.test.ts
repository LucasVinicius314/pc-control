import { describe, expect, it } from "vitest";
import { pickNext, type Box, type Candidate } from "./nav";

const box = (x: number, y: number, w = 100, h = 60): Box => ({ x, y, width: w, height: h });
const item = (id: number, x: number, y: number, w = 100, h = 60): Candidate => ({ id, box: box(x, y, w, h) });

describe("pickNext", () => {
  // Two rows of three:  1 2 3
  //                     4 5 6
  const grid = [item(1, 0, 0), item(2, 120, 0), item(3, 240, 0), item(4, 0, 80), item(5, 120, 80), item(6, 240, 80)];

  it("moves along a row", () => {
    expect(pickNext(grid[0]!.box, grid, "right")).toBe(2);
    expect(pickNext(grid[1]!.box, grid, "right")).toBe(3);
    expect(pickNext(grid[2]!.box, grid, "left")).toBe(2);
  });

  it("moves between rows, staying in its column", () => {
    expect(pickNext(grid[1]!.box, grid, "down")).toBe(5);
    expect(pickNext(grid[5]!.box, grid, "up")).toBe(3);
  });

  it("goes nowhere at the edge", () => {
    expect(pickNext(grid[0]!.box, grid, "left")).toBeNull();
    expect(pickNext(grid[0]!.box, grid, "up")).toBeNull();
    expect(pickNext(grid[5]!.box, grid, "right")).toBeNull();
    expect(pickNext(grid[5]!.box, grid, "down")).toBeNull();
  });

  it("does not pick itself", () => {
    expect(pickNext(grid[0]!.box, [grid[0]!], "right")).toBeNull();
  });

  it("stays on the row rather than jumping to a nearer item on another row", () => {
    // Item 9 is closer to the right of 1 but on a different row; 2 is on the same row.
    const cands = [item(2, 300, 0), item(9, 110, 70)];

    expect(pickNext(box(0, 0), cands, "right")).toBe(2);
  });

  it("falls back to the nearest item when nothing lines up", () => {
    const cands = [item(7, 300, 300), item(8, 150, 200)];

    expect(pickNext(box(0, 0), cands, "down")).toBe(8);
  });

  it("handles a tall item next to short ones", () => {
    const cands = [item(1, 200, 0, 100, 300), item(2, 0, 0)];

    expect(pickNext(box(0, 100, 100, 60), cands, "right")).toBe(1);
  });

  it("returns null for no candidates", () => {
    expect(pickNext(box(0, 0), [], "down")).toBeNull();
  });
});
