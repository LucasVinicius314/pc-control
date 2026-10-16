// Task-manager numbers: how they are written, and the small history behind the little graphs. No DOM
// here, so the phone page and the controller UI share it and it is tested without a browser.

export type PerfSort = "cpu" | "memory" | "gpu" | "disk" | "name";

export const PERF_SORTS: { id: PerfSort; label: string }[] = [
  { id: "cpu", label: "CPU" },
  { id: "memory", label: "Memory" },
  { id: "gpu", label: "GPU" },
  { id: "disk", label: "Disk" },
  { id: "name", label: "Name" },
];

const UNITS = ["B", "KB", "MB", "GB", "TB"];

/** "1.4 GB", "230 MB", "12 KB". Powers of 1024, like Windows. */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024;
    unit++;
  }
  // Show a decimal only where it says something: 1.4 GB, but 230 MB.
  const text = value >= 100 || unit === 0 ? Math.round(value).toString() : value.toFixed(1);
  return `${text} ${UNITS[unit]}`;
}

/** "2.0 MB/s". */
export function formatRate(bytesPerSecond: number): string {
  return `${formatBytes(bytesPerSecond)}/s`;
}

/** "37%", "0.6%", or a dash when the PC gives no figure. */
export function formatPercent(percent: number | null | undefined): string {
  if (percent === null || percent === undefined || !Number.isFinite(percent)) return "–";
  if (percent <= 0) return "0%";
  return percent < 10 ? `${percent.toFixed(1)}%` : `${Math.round(percent)}%`;
}

/** "12.4 / 32.0 GB" for memory, written in the larger unit. */
export function formatMemory(used: number, total: number): string {
  const unit = total >= 1024 ** 3 ? 1024 ** 3 : 1024 ** 2;
  const label = unit === 1024 ** 3 ? "GB" : "MB";
  return `${(used / unit).toFixed(1)} / ${(total / unit).toFixed(1)} ${label}`;
}

/** The last few readings of one number, for a small graph. */
export class History {
  private values: number[] = [];

  constructor(private readonly capacity = 40) {}

  push(value: number): void {
    this.values.push(Number.isFinite(value) ? value : 0);
    if (this.values.length > this.capacity) this.values.shift();
  }

  get all(): readonly number[] {
    return this.values;
  }
}

/**
 * The points of a line graph, as an SVG "points" value, filling `width` x `height`. `max` is the top of
 * the scale (100 for a percentage). Fewer readings than `capacity` start from the right, so the graph
 * grows in from the edge like Task Manager's.
 */
export function sparklinePoints(values: readonly number[], max: number, width: number, height: number, capacity = values.length): string {
  if (values.length === 0 || max <= 0) return "";
  const step = capacity > 1 ? width / (capacity - 1) : width;
  const offset = (capacity - values.length) * step;
  return values
    .map((v, i) => {
      const x = offset + i * step;
      const y = height - (Math.min(Math.max(v, 0), max) / max) * height;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");
}

/** The programs ordered by a column, busiest first (or A to Z for the name). Missing GPU figures sort last. */
export function sortProcesses<T extends { name: string; cpu: number; memory: number; gpu: number | null; io: number }>(processes: readonly T[], sort: PerfSort): T[] {
  // By name, A to Z; by anything else, the busiest first (and the name settles a tie).
  if (sort === "name") return [...processes].sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
  const key = (p: T): number => (sort === "cpu" ? p.cpu : sort === "memory" ? p.memory : sort === "gpu" ? (p.gpu ?? -1) : p.io);
  return [...processes].sort((a, b) => key(b) - key(a) || a.name.localeCompare(b.name));
}

/**
 * The same line as <see cref="sparklinePoints"/>, closed along the bottom, for the filled area under it.
 * Empty when there is no line.
 */
export function areaPoints(values: readonly number[], max: number, width: number, height: number, capacity = values.length): string {
  const line = sparklinePoints(values, max, width, height, capacity);
  if (line === "") return "";
  const points = line.split(" ");
  const firstX = points[0]!.split(",")[0]!;
  const lastX = points[points.length - 1]!.split(",")[0]!;
  return `${firstX},${height.toFixed(1)} ${line} ${lastX},${height.toFixed(1)}`;
}

/** The next sort in the list, wrapping round. */
export function nextSort(current: PerfSort): PerfSort {
  const index = PERF_SORTS.findIndex((s) => s.id === current);
  return PERF_SORTS[(index + 1) % PERF_SORTS.length]!.id;
}

/** A program's name with how many processes it is, "chrome ×12". */
export function programLabel(name: string, count: number): string {
  return count > 1 ? `${name} ×${count}` : name;
}
