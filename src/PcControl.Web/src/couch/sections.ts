// The screens of the controller UI: games, apps, media, running programs, and the PC itself.

import { api, ApiError, type AppItem, type PerformanceData } from "../api";
import { buildSections, launchFailureMessage } from "../appsview";
import { icon, type IconName } from "../icons";
import { POWER_BUTTONS } from "../system";
import { changedMessage, failureMessage } from "../dpi";
import { ConfirmGate, needsConfirmation, powerDone, powerMessage, processMessage, countdownText, type PowerName } from "../systemview";
import { buildProcessRows, createStatCards } from "../perf";
import { formatBytes, nextSort, PERF_SORTS, programLabel, sortProcesses, type PerfSort } from "../perfview";
import { REMOTE_CLUSTERS, REMOTE_MAIN, type RemoteButton } from "../media";
import { cover, el, heading, makeItem, message, shelf, type Hint } from "./ui";

/** What a screen can ask of the shell around it. */
export interface Shell {
  toast: (text: string) => void;
  /** Hide the controller UI, for example after a game started so it can be seen. */
  hide: () => void;
  /** Show a picture behind everything (a game's background), or none. `soft` blurs it, for box art rather than a wide background. */
  backdrop: (url: string | null, soft?: boolean) => void;
  pressKey: (code: string) => void;
  /** Read the current screen again, keeping the focus about where it was. */
  refresh: () => void;
  /** Move the focus to an item. */
  focus: (item: HTMLElement) => void;
}

export interface Section {
  id: string;
  title: string;
  icon: IconName;
  element: HTMLElement;
  /** Called each time the screen is shown: read the lists again. Returns the item to focus first, if any. */
  show: () => Promise<HTMLElement | null>;
  /** The footer when no item is focused (or the item has no hints of its own). */
  hints: Hint[];
  /** Called when the user leaves this screen, to stop anything running in the background. */
  leave?: () => void;
  /** Start / Back button on this screen, if it has something to switch (for example tiles or a list). */
  menu?: () => void;
}

const A_SELECT: Hint = { button: "A", label: "Select" };

function section(id: string, title: string, iconName: IconName, hints: Hint[]): Section & { fill: (nodes: HTMLElement[]) => void } {
  const element = el("section", `screen screen-${id}`);
  element.hidden = true;
  return {
    id,
    title,
    icon: iconName,
    element,
    hints,
    show: async () => null,
    fill: (nodes) => element.replaceChildren(...nodes),
  };
}

/** The first focusable item inside a screen. */
const firstItem = (root: HTMLElement): HTMLElement | null => root.querySelector<HTMLElement>("[data-focusable]");

// ---- games ---------------------------------------------------------------------------------------

/** The emulators games come from, in the order their shelves are shown. `tall` is for box art (PS2); the rest is wide (PS3 icons). */
const GAME_SOURCES = [
  { id: "rpcs3", name: "RPCS3", tall: false },
  { id: "pcsx2", name: "PCSX2", tall: true },
];

export function gamesSection(shell: Shell): Section {
  const s = section("games", "Games", "gamepad", [A_SELECT, { button: "B", label: "Close" }]);

  const card = (game: { id: string; title: string; serial: string; source: string }): HTMLElement => {
    const c = el("div", "card game");
    c.append(cover(`/api/games/${game.id}/cover`, game.title, "cover", true), el("div", "card-name", game.title), el("div", "card-sub", game.serial));
    return makeItem(c, {
      accept: async () => {
        shell.toast(`Starting ${game.title}…`);
        try {
          await api.launchGame(game.id);
          shell.hide();
        } catch (error) {
          shell.toast(launchFailureMessage(error, game.title));
        }
      },
      focus: () => shell.backdrop(`/api/games/${game.id}/background`, game.source !== "rpcs3"),
      hints: [{ button: "A", label: "Play" }, { button: "B", label: "Close" }],
    });
  };

  s.show = async () => {
    try {
      const data = await api.games();
      if (!data.available) {
        s.fill([message("No games found", data.reason ?? "RPCS3 was not found on this PC.")]);
        return null;
      }
      if (data.games.length === 0) {
        s.fill([message("No games yet", "Add games in RPCS3 or PCSX2 and they will show up here.")]);
        return null;
      }

      const byId = new Map(data.games.map((g) => [g.id, g]));
      const recent = data.recent.map((id) => byId.get(id)).filter((g) => g !== undefined);
      const nodes: HTMLElement[] = [];
      if (recent.length > 0) nodes.push(heading("Continue playing"), shelf(...recent.slice(0, 6).map(card)));
      // One shelf per emulator, in a fixed order; a source this page does not know about goes last, under its own name.
      const known = new Set(GAME_SOURCES.map((g) => g.id));
      const groups = [
        ...GAME_SOURCES.map((g) => ({ name: g.name, tall: g.tall, games: data.games.filter((x) => x.source === g.id) })),
        ...[...new Set(data.games.filter((x) => !known.has(x.source)).map((x) => x.source))].map((id) => ({ name: id.toUpperCase(), tall: false, games: data.games.filter((x) => x.source === id) })),
      ];
      for (const group of groups) {
        if (group.games.length > 0) {
          const row = shelf(...group.games.map(card));
          row.classList.add("games", group.tall ? "tall" : "wide");
          nodes.push(heading(group.name), row);
        }
      }
      s.fill(nodes);
      return firstItem(s.element);
    } catch (error) {
      s.fill([message("Could not load the games", error instanceof ApiError ? error.code : undefined)]);
      return null;
    }
  };
  return s;
}

// ---- apps ----------------------------------------------------------------------------------------

const APPS_VIEW_KEY = "pc-control.couch.appsView";

type AppsView = "grid" | "list";

function loadAppsView(): AppsView {
  try {
    return localStorage.getItem(APPS_VIEW_KEY) === "list" ? "list" : "grid";
  } catch {
    return "grid";
  }
}

export function appsSection(shell: Shell): Section {
  const s = section("apps", "Apps", "apps", [A_SELECT, { button: "B", label: "Close" }]);
  let view = loadAppsView();

  const load = async (): Promise<HTMLElement | null> => {
    try {
      const data = await api.apps();
      const switchHint: Hint = { button: "Start", label: view === "grid" ? "List view" : "Tile view" };

      const card = (app: AppItem): HTMLElement => {
        const c = el("div", view === "grid" ? "card app" : "row app-row");
        if (view === "grid") {
          c.append(cover(`/api/apps/${app.id}/icon`, app.name, "app-icon"), el("div", "card-title", app.name));
        } else {
          c.append(cover(`/api/apps/${app.id}/icon`, app.name, "app-icon small"), el("div", "row-title", app.name));
          if (app.favorite) c.append(el("span", "row-star", "\u2605"));
        }
        return makeItem(c, {
          accept: async () => {
            try {
              const result = await api.launchApp(app.id);
              shell.toast(`Launching ${result.name}…`);
              shell.hide();
            } catch (error) {
              shell.toast(launchFailureMessage(error, app.name));
            }
          },
          y: async () => {
            try {
              await api.setFavorite(app.id, !app.favorite);
              shell.toast(app.favorite ? `${app.name} removed from favorites` : `${app.name} added to favorites`);
              shell.refresh();
            } catch (error) {
              shell.toast(launchFailureMessage(error, app.name));
            }
          },
          hints: [{ button: "A", label: "Open" }, { button: "Y", label: app.favorite ? "Unfavorite" : "Favorite" }, switchHint, { button: "B", label: "Close" }],
        });
      };

      const nodes: HTMLElement[] = [];
      for (const group of buildSections(data.apps, data.recent, "")) {
        const items = group.apps.map(card);
        if (view === "grid") {
          nodes.push(heading(group.title), shelf(...items));
        } else {
          const list = el("div", "list");
          list.append(...items);
          nodes.push(heading(group.title), list);
        }
      }
      s.fill(nodes);
      s.hints = [A_SELECT, switchHint, { button: "B", label: "Close" }];
      return firstItem(s.element);
    } catch (error) {
      s.fill([message("Could not load the apps", error instanceof ApiError ? error.code : undefined)]);
      return null;
    }
  };

  s.show = load;
  s.menu = () => {
    view = view === "grid" ? "list" : "grid";
    try {
      localStorage.setItem(APPS_VIEW_KEY, view);
    } catch {
      // Not remembered; the next start shows tiles again.
    }
    shell.refresh();
  };
  return s;
}

// ---- media ---------------------------------------------------------------------------------------

/** The media screen: a remote, small and in the bottom right where the eye rests while watching. */
export function mediaSection(shell: Shell): Section {
  const s = section("media", "Media", "media", [{ button: "A", label: "Press" }, { button: "B", label: "Close" }]);

  const round = (b: RemoteButton, size: "lg" | "md" | "sm"): HTMLElement => {
    const r = el("div", `round ${size}${b.primary ? " primary" : ""}`);
    r.append(icon(b.icon));
    if (b.seconds) r.append(el("span", "round-seconds", b.seconds));
    return makeItem(r, { accept: () => shell.pressKey(b.code), hints: [{ button: "A", label: b.hint }, { button: "B", label: "Close" }] });
  };

  const remote = el("div", "media-remote");
  const main = el("div", "media-main");
  main.append(...REMOTE_MAIN.map((b) => round(b, b.primary ? "lg" : "md")));

  const extras = el("div", "media-extras");
  for (const cluster of REMOTE_CLUSTERS) {
    const group = el("div", "media-cluster");
    const buttons = el("div", "media-cluster-buttons");
    buttons.append(...cluster.buttons.map((b) => round(b, "sm")));
    group.append(buttons, el("div", "media-caption", cluster.caption));
    extras.append(group);
  }

  remote.append(main, extras);
  s.fill([remote]);
  s.show = async () => s.element.querySelector<HTMLElement>(".round.primary");
  return s;
}

// ---- running programs ----------------------------------------------------------------------------

const RUNNING_VIEW_KEY = "pc-control.couch.runningView";

type RunningView = "windows" | "list";

function loadRunningView(): RunningView {
  try {
    return localStorage.getItem(RUNNING_VIEW_KEY) === "list" ? "list" : "windows";
  } catch {
    return "windows";
  }
}

/** The running programs, as windows with their pictures (like Alt+Tab) or as a plain list. */
export function runningSection(shell: Shell): Section {
  const s = section("running", "Running", "window", [A_SELECT, { button: "B", label: "Close" }]);
  const gate = new ConfirmGate();
  let view = loadRunningView();

  const load = async (): Promise<HTMLElement | null> => {
    try {
      const items = await api.processes();
      if (items.length === 0) {
        s.fill([message("Nothing is running", "Programs with a window show up here.")]);
        return null;
      }

      const switchHint: Hint = { button: "Start", label: view === "windows" ? "List view" : "Window view" };
      // One stamp per load, so the pictures are fresh each time this screen is shown.
      const stamp = Date.now();

      const rows = items.map((item) => {
        const previewUrl = `/api/processes/${item.id}/preview?t=${stamp}`;
        const row = el("div", view === "windows" ? "card win" : "row");
        if (view === "windows") {
          const shot = cover(previewUrl, item.name, "cover", true);
          if (item.minimized) shot.append(el("span", "win-badge", "Minimized"));
          row.append(shot, el("div", "card-name", item.title), el("div", "card-sub", item.name));
        } else {
          row.append(el("div", "row-title", item.title), el("div", "row-sub", item.name));
        }
        return makeItem(row, {
          accept: async () => {
            try {
              await api.focusProcess(item.id);
              shell.hide();
            } catch (error) {
              shell.toast(processMessage(error));
            }
          },
          x: async () => {
            try {
              await api.closeProcess(item.id, false);
              shell.toast(`Asked ${item.title} to close`);
            } catch (error) {
              shell.toast(processMessage(error));
            }
            window.setTimeout(() => void s.show(), 800);
          },
          y: async () => {
            if (!gate.tap(item.id)) {
              shell.toast(`Press Y again to force close ${item.title}. Unsaved work is lost.`);
              return;
            }
            try {
              await api.closeProcess(item.id, true);
              shell.toast(`Force closed ${item.title}`);
            } catch (error) {
              shell.toast(processMessage(error));
            }
            window.setTimeout(() => void s.show(), 800);
          },
          // The focused window's picture, blurred, behind the screen.
          focus: () => shell.backdrop(view === "windows" && !item.minimized ? previewUrl : null, true),
          hints: [{ button: "A", label: "Switch to" }, { button: "X", label: "Close" }, { button: "Y", label: "Force close" }, switchHint, { button: "B", label: "Close menu" }],
        });
      });

      if (view === "windows") {
        const grid = shelf(...rows);
        grid.classList.add("windows");
        s.fill([heading("Open windows"), grid]);
      } else {
        const list = el("div", "list");
        list.append(...rows);
        s.fill([heading("Open programs"), list]);
      }
      s.hints = [A_SELECT, switchHint, { button: "B", label: "Close" }];
      return rows[0] ?? null;
    } catch (error) {
      s.fill([message("Could not load the programs", error instanceof ApiError ? error.code : undefined)]);
      return null;
    }
  };

  s.show = load;
  s.menu = () => {
    view = view === "windows" ? "list" : "windows";
    try {
      localStorage.setItem(RUNNING_VIEW_KEY, view);
    } catch {
      // Not remembered; the next start shows windows again.
    }
    shell.refresh();
  };
  return s;
}

// ---- the PC --------------------------------------------------------------------------------------

const ARM_MS = 4000;

interface PcRowOptions {
  icon: IconName;
  title: string;
  description: string;
  danger?: boolean;
  value?: string;
}

/** A row in a card: an icon, a title, a line of explanation, and optionally a value on the right. */
function pcRow(o: PcRowOptions): { row: HTMLElement; desc: HTMLElement; value: HTMLElement | null } {
  const row = el("div", `pc-row${o.danger ? " danger" : ""}`);
  const iconBox = el("div", "pc-icon");
  iconBox.append(icon(o.icon));
  const text = el("div", "pc-text");
  const desc = el("div", "pc-desc", o.description);
  text.append(el("div", "pc-title", o.title), desc);
  row.append(iconBox, text);
  const value = o.value !== undefined ? el("div", "pc-value", o.value) : null;
  if (value) row.append(value);
  return { row, desc, value };
}

function pcCard(title: string, rows: HTMLElement[]): HTMLElement {
  const card = el("div", "pc-card");
  card.append(el("div", "pc-card-title", title), ...rows);
  return card;
}

export function pcSection(shell: Shell, features: { power: boolean; dpi: boolean }): Section {
  const s = section("pc", "PC", "power", [A_SELECT, { button: "B", label: "Close" }]);
  const gate = new ConfirmGate(ARM_MS);

  // ---- a restart or shutdown that is counting down ---------------------------------------------
  const pending = el("div", "pc-pending");
  pending.hidden = true;
  const pendingText = el("div", "pc-pending-text");
  let pendingTimer = 0;

  const cancel = pcRow({ icon: "closeX", title: "Cancel", description: "Stop it" });
  makeItem(cancel.row, {
    accept: async () => {
      try {
        await api.cancelPower();
        shell.toast("Cancelled");
      } catch (error) {
        shell.toast(powerMessage(error));
      }
      await showPending();
    },
    hints: [{ button: "A", label: "Cancel the countdown" }],
  });
  pending.append(pendingText, cancel.row);

  async function showPending(): Promise<void> {
    window.clearInterval(pendingTimer);
    try {
      const { pending: p } = await api.powerStatus();
      pending.hidden = p === null;
      if (p === null) return;
      let left = p.secondsLeft;
      pendingText.textContent = countdownText(p.action, left);
      pendingTimer = window.setInterval(() => {
        left -= 1;
        if (left <= 0) {
          window.clearInterval(pendingTimer);
          pending.hidden = true;
          return;
        }
        pendingText.textContent = countdownText(p.action, left);
      }, 1000);
    } catch {
      pending.hidden = true;
    }
  }

  // ---- power rows ------------------------------------------------------------------------------
  const descriptions: Record<PowerName, string> = {
    lock: "Go to the sign-in screen",
    sleep: "Save power, wake in a moment",
    signOut: "Close your apps and end this session",
    restart: "Restart the PC in 15 seconds",
    shutdown: "Turn the PC off in 15 seconds",
  };

  const powerRow = (action: PowerName): HTMLElement => {
    const p = POWER_BUTTONS.find((b) => b.action === action)!;
    const dangerous = needsConfirmation(action);
    const { row, desc } = pcRow({ icon: p.icon, title: p.label, description: descriptions[action], danger: dangerous });
    let disarm = 0;

    const reset = (): void => {
      window.clearTimeout(disarm);
      row.classList.remove("armed");
      desc.textContent = descriptions[action];
    };

    return makeItem(row, {
      accept: async () => {
        if (dangerous && !gate.tap(action)) {
          // First press: the row turns red, says what to do, and shows a shrinking bar for the time left.
          row.classList.add("armed");
          desc.textContent = "Press A again to confirm";
          window.clearTimeout(disarm);
          disarm = window.setTimeout(reset, ARM_MS);
          return;
        }
        reset();
        gate.reset();
        try {
          const result = await api.power(action, dangerous);
          shell.toast(powerDone(action, result.delaySeconds));
          if (result.delaySeconds > 0) void showPending();
        } catch (error) {
          shell.toast(powerMessage(error));
        }
      },
      hints: [{ button: "A", label: dangerous ? `${p.label} (press twice)` : p.label }, { button: "B", label: "Close" }],
    });
  };

  const cards: HTMLElement[] = [];
  if (features.power) {
    cards.push(
      pcCard("Session", [powerRow("lock"), powerRow("sleep"), powerRow("signOut")]),
      pcCard("Power", [powerRow("restart"), powerRow("shutdown")]),
    );
  }

  // ---- display scale ---------------------------------------------------------------------------
  let dpiPercents: { current: number; other: number } | null = null;
  const display = pcRow({ icon: "dpi", title: "Display scale", description: "Make everything bigger or smaller", value: "–" });
  const describeScale = (): void => {
    if (display.value) display.value.textContent = dpiPercents ? `${dpiPercents.current}%` : "–";
    display.desc.textContent = dpiPercents ? `Press A to switch to ${dpiPercents.other}%` : "Make everything bigger or smaller";
  };
  if (features.dpi) {
    makeItem(display.row, {
      accept: async () => {
        try {
          const change = await api.toggleDpi();
          dpiPercents = { current: change.percent, other: change.previous };
          describeScale();
          shell.toast(changedMessage(change));
        } catch (error) {
          shell.toast(failureMessage(error));
        }
      },
      hints: [{ button: "A", label: "Switch the display scale" }, { button: "B", label: "Close" }],
    });
    cards.push(pcCard("Display", [display.row]));
  }

  const grid = el("div", "pc-grid");
  grid.append(...cards);
  s.fill([pending, grid]);

  s.show = async () => {
    gate.reset();
    if (features.power) await showPending();
    if (features.dpi) {
      try {
        const status = await api.dpi();
        if (status.available && status.percent) {
          dpiPercents = { current: status.percent, other: status.percent === status.lowPercent ? status.highPercent : status.lowPercent };
        } else {
          dpiPercents = null;
        }
      } catch {
        dpiPercents = null;
      }
      describeScale();
    }
    return firstItem(s.element);
  };
  s.leave = () => window.clearInterval(pendingTimer);
  return s;
}

// ---- performance ---------------------------------------------------------------------------------

const PERF_POLL_MS = 1500;
const PERF_ROWS = Number.POSITIVE_INFINITY; // every program; the list scrolls

export function performanceSection(shell: Shell): Section {
  const s = section("performance", "Performance", "chart", [{ button: "B", label: "Close" }]);
  const stats = createStatCards();
  const head = el("div", "perf-head-holder");
  const list = el("div", "perf-list");
  list.append(message("Reading the PC…", "The first reading takes a moment."));
  s.fill([stats.element, head, list]);

  let sort: PerfSort = "cpu";
  let last: PerformanceData | null = null;
  let timer = 0;
  let busy = false;
  let again = false; // a reading was asked for while one was running: do another straight after
  let active = false;

  const sortHint = (): Hint => ({ button: "X", label: `Sort by ${PERF_SORTS.find((p) => p.id === nextSort(sort))!.label}` });

  /** Draws the programs from the data we already have, in the current order. The focus stays on the same program. */
  const draw = (): void => {
    if (!last) return;
    const data = last;
    const focusedName = list.querySelector<HTMLElement>(".focused")?.dataset["name"];
    const [header, ...rows] = buildProcessRows(data, sort, "row perf-line", PERF_ROWS);
    const shown = sortProcesses(data.processes, sort).slice(0, PERF_ROWS);
    let keep: HTMLElement | null = null;
    rows.forEach((row, i) => {
      const p = shown[i]!;
      row.dataset["name"] = p.name;
      makeItem(row, { x: changeSort, hints: [sortHint(), { button: "B", label: "Close" }] });
      row.title = `${programLabel(p.name, p.count)}, ${formatBytes(p.memory)}`;
      if (p.name === focusedName) keep = row;
    });
    head.replaceChildren(header!);
    list.replaceChildren(...rows);
    if (keep) shell.focus(keep);
  };

  const refresh = async (): Promise<void> => {
    if (!active) return;
    if (busy) {
      again = true;
      return;
    }
    busy = true;
    try {
      // All programs, once; the order is chosen here, so changing it never waits for the PC.
      last = await api.performance("cpu", 1000);
      stats.update(last);
      draw();
    } catch {
      // The next reading tries again.
    } finally {
      busy = false;
      if (again) {
        again = false;
        void refresh();
      }
    }
  };

  function changeSort(): void {
    sort = nextSort(sort);
    s.hints = [sortHint(), { button: "B", label: "Close" }];
    draw(); // the new order shows straight away...
    void refresh(); // ...and fresh numbers are asked for at once, not at the next tick
  }

  s.show = async () => {
    active = true;
    window.clearInterval(timer);
    timer = window.setInterval(() => void refresh(), PERF_POLL_MS);
    s.hints = [sortHint(), { button: "B", label: "Close" }];
    draw(); // what we had last time, immediately, while the new reading is on its way
    await refresh();
    return firstItem(s.element);
  };
  s.leave = () => {
    active = false;
    window.clearInterval(timer);
    timer = 0;
  };
  return s;
}
