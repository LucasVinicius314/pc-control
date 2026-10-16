// The performance view on the phone: how busy the PC is, and which programs are using it.

import { api, type PerformanceData } from "./api";
import { sortProcesses, formatMemory, formatPercent, formatRate, formatBytes, History, PERF_SORTS, programLabel, sparklinePoints, areaPoints, type PerfSort } from "./perfview";

const POLL_MS = 2000;
const HISTORY = 40;
const GRAPH_W = 100;
const GRAPH_H = 28;
const SVG_NS = "http://www.w3.org/2000/svg";

export interface PerformanceView {
  element: HTMLElement;
  /** Start reading. Reading stops by itself whenever the view is not on screen. */
  start: () => void;
}

interface Card {
  box: HTMLElement;
  value: HTMLElement;
  detail: HTMLElement;
  line: SVGPolylineElement;
  area: SVGPolygonElement;
  /** The label at the top of the graph: what the top edge stands for (100% or the busiest traffic seen). */
  top: HTMLElement;
  history: History;
}

/**
 * One summary card. The graph sits in a track that is always the full range (the bottom edge is 0, the
 * top edge is 100% or the labelled maximum), with the area under the line filled, so how full it is can
 * be read at a glance and a low figure looks low rather than "offset".
 */
function card(title: string, topLabel = "100%"): Card {
  const box = document.createElement("div");
  box.className = "perf-card";
  const name = document.createElement("div");
  name.className = "perf-card-title";
  name.textContent = title;
  const value = document.createElement("div");
  value.className = "perf-card-value";
  const detail = document.createElement("div");
  detail.className = "perf-card-detail";

  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", `0 0 ${GRAPH_W} ${GRAPH_H}`);
  svg.setAttribute("preserveAspectRatio", "none");
  svg.setAttribute("aria-hidden", "true");
  svg.classList.add("perf-graph");

  const track = document.createElementNS(SVG_NS, "rect");
  track.setAttribute("width", String(GRAPH_W));
  track.setAttribute("height", String(GRAPH_H));
  track.classList.add("perf-track");
  const middle = document.createElementNS(SVG_NS, "line");
  middle.setAttribute("x1", "0");
  middle.setAttribute("x2", String(GRAPH_W));
  middle.setAttribute("y1", String(GRAPH_H / 2));
  middle.setAttribute("y2", String(GRAPH_H / 2));
  middle.setAttribute("vector-effect", "non-scaling-stroke");
  middle.classList.add("perf-mid");
  const area = document.createElementNS(SVG_NS, "polygon");
  area.classList.add("perf-area");
  const line = document.createElementNS(SVG_NS, "polyline");
  line.setAttribute("fill", "none");
  line.setAttribute("stroke", "currentColor");
  line.setAttribute("stroke-width", "1.8");
  line.setAttribute("stroke-linejoin", "round");
  line.setAttribute("vector-effect", "non-scaling-stroke");
  svg.append(track, middle, area, line);

  const top = document.createElement("span");
  top.className = "perf-scale perf-scale-top";
  top.textContent = topLabel;
  const bottom = document.createElement("span");
  bottom.className = "perf-scale perf-scale-bottom";
  bottom.textContent = "0";
  const graph = document.createElement("div");
  graph.className = "perf-graph-box";
  graph.append(svg, top, bottom);

  box.append(name, value, detail, graph);
  return { box, value, detail, line, area, top, history: new History(HISTORY) };
}

/** The five summary cards (CPU, GPU, memory, disk, network) with their little graphs. */
export interface StatCards {
  element: HTMLElement;
  update: (data: PerformanceData) => void;
}

export function createStatCards(): StatCards {
  const cpu = card("CPU");
  const gpu = card("GPU");
  const memory = card("Memory");
  const disk = card("Disk");
  const network = card("Network", "peak");
  const networkUp = new History(HISTORY);

  const element = document.createElement("div");
  element.className = "perf-cards";
  element.append(cpu.box, gpu.box, memory.box, disk.box, network.box);

  const draw = (c: Card, max: number): void => {
    c.line.setAttribute("points", sparklinePoints(c.history.all, max, GRAPH_W, GRAPH_H, HISTORY));
    c.area.setAttribute("points", areaPoints(c.history.all, max, GRAPH_W, GRAPH_H, HISTORY));
  };

  const update = (data: PerformanceData): void => {
    cpu.value.textContent = formatPercent(data.cpu);
    cpu.detail.textContent = `${data.cores} threads`;
    cpu.history.push(data.cpu);
    draw(cpu, 100);

    gpu.box.hidden = data.gpu === null;
    if (data.gpu !== null) {
      gpu.value.textContent = formatPercent(data.gpu);
      gpu.detail.textContent = "busiest engine";
      gpu.history.push(data.gpu);
      draw(gpu, 100);
    }

    const usedPercent = data.memory.total > 0 ? (data.memory.used / data.memory.total) * 100 : 0;
    memory.value.textContent = formatPercent(usedPercent);
    memory.detail.textContent = formatMemory(data.memory.used, data.memory.total);
    memory.history.push(usedPercent);
    draw(memory, 100);

    disk.value.textContent = formatPercent(data.disk.percent);
    disk.detail.textContent = `read ${formatRate(data.disk.readPerSecond)} · write ${formatRate(data.disk.writePerSecond)}`;
    disk.history.push(data.disk.percent ?? 0);
    draw(disk, 100);

    network.value.textContent = `↓ ${formatRate(data.network.downPerSecond)}`;
    network.detail.textContent = `↑ ${formatRate(data.network.upPerSecond)}`;
    network.history.push(data.network.downPerSecond);
    networkUp.push(data.network.upPerSecond);
    const scale = Math.max(1024 * 1024, ...network.history.all, ...networkUp.all); // a scale that grows with the traffic
    network.top.textContent = formatRate(scale);
    draw(network, scale);
  };

  return { element, update };
}

/** One row per program: name, then CPU, memory, GPU and disk. `sorted` highlights the column it is ordered by. */
export function buildProcessRows(data: PerformanceData, sorted: PerfSort, rowClass = "perf-row", limit = Number.POSITIVE_INFINITY): HTMLElement[] {
  const header = document.createElement("div");
  header.className = `${rowClass} perf-head`;
  for (const text of ["Program", "CPU", "Memory", "GPU", "Disk"]) {
    const cell = document.createElement("span");
    cell.textContent = text;
    header.append(cell);
  }

  const rows = sortProcesses(data.processes, sorted).slice(0, limit).map((p) => {
    const row = document.createElement("div");
    row.className = rowClass;
    const name = document.createElement("span");
    name.className = "perf-name";
    name.textContent = programLabel(p.name, p.count);
    name.title = programLabel(p.name, p.count);
    if (sorted === "name") name.classList.add("sorted");
    const cells = [formatPercent(p.cpu), formatBytes(p.memory), formatPercent(p.gpu), formatRate(p.io)].map((text, i) => {
      const cell = document.createElement("span");
      cell.textContent = text;
      if (PERF_SORTS[i]!.id === sorted) cell.classList.add("sorted");
      return cell;
    });
    row.append(name, ...cells);
    return row;
  });
  return [header, ...rows];
}

export function createPerformanceView(): PerformanceView {
  const element = document.createElement("section");
  element.className = "hub-pane perf";
  element.hidden = true;

  const stats = createStatCards();

  let sort: PerfSort = "cpu";
  const sortBar = document.createElement("div");
  sortBar.className = "segmented perf-sort";
  const sortButtons = new Map<PerfSort, HTMLButtonElement>();
  for (const s of PERF_SORTS) {
    const b = document.createElement("button");
    b.type = "button";
    b.textContent = s.label;
    b.addEventListener("click", () => {
      sort = s.id;
      for (const [id, button] of sortButtons) button.classList.toggle("on", id === sort);
      // Show the new order at once from what we already have, then ask for fresh numbers.
      if (last) table.replaceChildren(...buildProcessRows(last, sort));
      void refresh();
    });
    sortButtons.set(s.id, b);
    sortBar.append(b);
  }
  sortButtons.get(sort)!.classList.add("on");

  const table = document.createElement("div");
  table.className = "perf-table";
  const waiting = document.createElement("p");
  waiting.className = "sys-empty";
  waiting.textContent = "Reading the PC… the first reading takes a moment.";
  table.append(waiting);

  element.append(stats.element, sortBar, table);

  let last: PerformanceData | null = null;
  let busy = false;
  const refresh = async (): Promise<void> => {
    if (busy) return;
    busy = true;
    try {
      last = await api.performance("cpu", 1000); // every program; the order is chosen here
      stats.update(last);
      table.replaceChildren(...buildProcessRows(last, sort));
    } catch {
      // The numbers are a convenience; the next reading tries again.
    } finally {
      busy = false;
    }
  };

  // Read while the view is on screen (it and everything around it visible, and the page in the foreground).
  let timer = 0;
  const start = (): void => {
    void refresh();
    if (timer !== 0) return;
    timer = window.setInterval(() => {
      if (element.offsetParent === null || document.visibilityState !== "visible") {
        window.clearInterval(timer);
        timer = 0;
        return;
      }
      void refresh();
    }, POLL_MS);
  };

  return { element, start };
}
