import { describe, expect, it } from "vitest";
import { areaPoints, sortProcesses, formatBytes, formatMemory, formatPercent, formatRate, History, nextSort, programLabel, sparklinePoints } from "./perfview";

describe("formatBytes", () => {
  it("uses the unit that reads best", () => {
    expect(formatBytes(0)).toBe("0 B");
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
    expect(formatBytes(230 * 1024 * 1024)).toBe("230 MB");
    expect(formatBytes(1.4 * 1024 ** 3)).toBe("1.4 GB");
  });

  it("copes with nonsense", () => {
    expect(formatBytes(-5)).toBe("0 B");
    expect(formatBytes(Number.NaN)).toBe("0 B");
    expect(formatBytes(Number.POSITIVE_INFINITY)).toBe("0 B");
  });

  it("stops at terabytes", () => {
    expect(formatBytes(5 * 1024 ** 5)).toBe("5120 TB");
  });
});

describe("formatRate", () => {
  it("adds per second", () => {
    expect(formatRate(2 * 1024 * 1024)).toBe("2.0 MB/s");
    expect(formatRate(0)).toBe("0 B/s");
  });
});

describe("formatPercent", () => {
  it("shows a decimal only for small numbers", () => {
    expect(formatPercent(37.4)).toBe("37%");
    expect(formatPercent(0.64)).toBe("0.6%");
    expect(formatPercent(9.96)).toBe("10.0%");
    expect(formatPercent(100)).toBe("100%");
    expect(formatPercent(0)).toBe("0%");
  });

  it("shows a dash when there is no figure", () => {
    expect(formatPercent(null)).toBe("–");
    expect(formatPercent(undefined)).toBe("–");
    expect(formatPercent(Number.NaN)).toBe("–");
  });
});

describe("formatMemory", () => {
  it("is written in gigabytes for a big PC", () => {
    expect(formatMemory(12.4 * 1024 ** 3, 32 * 1024 ** 3)).toBe("12.4 / 32.0 GB");
  });

  it("falls back to megabytes for a small one", () => {
    expect(formatMemory(512 * 1024 ** 2, 768 * 1024 ** 2)).toBe("512.0 / 768.0 MB");
  });
});

describe("History", () => {
  it("keeps only the newest readings", () => {
    const h = new History(3);
    for (const v of [1, 2, 3, 4, 5]) h.push(v);

    expect(h.all).toEqual([3, 4, 5]);
  });

  it("turns a bad reading into zero", () => {
    const h = new History(3);
    h.push(Number.NaN);

    expect(h.all).toEqual([0]);
  });
});

describe("sparklinePoints", () => {
  it("draws zero at the bottom and the maximum at the top", () => {
    expect(sparklinePoints([0, 100], 100, 10, 20)).toBe("0.0,20.0 10.0,0.0");
  });

  it("clamps readings outside the scale", () => {
    expect(sparklinePoints([-5, 500], 100, 10, 20)).toBe("0.0,20.0 10.0,0.0");
  });

  it("grows in from the right while there are few readings", () => {
    expect(sparklinePoints([50], 100, 30, 10, 4)).toBe("30.0,5.0");
    expect(sparklinePoints([0, 100], 100, 30, 10, 4)).toBe("20.0,10.0 30.0,0.0");
  });

  it("is empty with no readings or no scale", () => {
    expect(sparklinePoints([], 100, 10, 10)).toBe("");
    expect(sparklinePoints([1], 0, 10, 10)).toBe("");
  });
});

describe("sorting and labels", () => {
  it("cycles through the columns", () => {
    expect(nextSort("cpu")).toBe("memory");
    expect(nextSort("memory")).toBe("gpu");
    expect(nextSort("gpu")).toBe("disk");
    expect(nextSort("disk")).toBe("name");
    expect(nextSort("name")).toBe("cpu");
  });

  it("shows how many processes a program has", () => {
    expect(programLabel("chrome", 12)).toBe("chrome ×12");
    expect(programLabel("notepad", 1)).toBe("notepad");
  });
});

describe("sortProcesses", () => {
  const list = [
    { name: "b", cpu: 5, memory: 100, gpu: null, io: 9 },
    { name: "a", cpu: 5, memory: 300, gpu: 0, io: 1 },
    { name: "c", cpu: 20, memory: 200, gpu: 50, io: 5 },
  ];

  it("puts the busiest first by the chosen column", () => {
    expect(sortProcesses(list, "cpu").map((p) => p.name)).toEqual(["c", "a", "b"]);
    expect(sortProcesses(list, "memory").map((p) => p.name)).toEqual(["a", "c", "b"]);
    expect(sortProcesses(list, "gpu").map((p) => p.name)).toEqual(["c", "a", "b"]);
    expect(sortProcesses(list, "disk").map((p) => p.name)).toEqual(["b", "c", "a"]);
  });

  it("breaks ties by name and leaves the original list alone", () => {
    const before = list.map((p) => p.name);

    expect(sortProcesses(list, "cpu").slice(1).map((p) => p.name)).toEqual(["a", "b"]);
    expect(list.map((p) => p.name)).toEqual(before);
  });
});

describe("sort by name", () => {
  it("goes A to Z, ignoring case", () => {
    const list = [
      { name: "chrome", cpu: 50, memory: 1, gpu: null, io: 0 },
      { name: "Audacity", cpu: 1, memory: 1, gpu: null, io: 0 },
      { name: "discord", cpu: 9, memory: 1, gpu: null, io: 0 },
    ];

    expect(sortProcesses(list, "name").map((p) => p.name)).toEqual(["Audacity", "chrome", "discord"]);
  });
});

describe("areaPoints", () => {
  it("closes the line along the bottom", () => {
    expect(areaPoints([0, 100], 100, 10, 20)).toBe("0.0,20.0 0.0,20.0 10.0,0.0 10.0,20.0");
  });

  it("is empty without a line", () => {
    expect(areaPoints([], 100, 10, 20)).toBe("");
  });
});
