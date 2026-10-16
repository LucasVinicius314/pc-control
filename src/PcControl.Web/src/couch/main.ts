import "./couch.css";
import { api, ApiError, type Capabilities } from "../api";
import { icon } from "../icons";
import { InputChannel } from "../input";
import { Link } from "../socket";
import { applyTheme, DEFAULT_THEME, loadCachedTheme } from "../theme";
import { pickNext, type Direction } from "./nav";
import { Sounds } from "./sounds";
import { actionForKey, PadReader, StepLimiter, type PadAction } from "./pad";
import { appsSection, gamesSection, mediaSection, pcSection, performanceSection, runningSection, type Section, type Shell } from "./sections";
import { el, handlersOf, renderHints, type Hint } from "./ui";

// The controller UI runs inside a window of the PC Control app. This is how it talks to that window.
interface HostBridge {
  postMessage: (message: string) => void;
}
const host = (window as unknown as { chrome?: { webview?: HostBridge } }).chrome?.webview;

applyTheme(loadCachedTheme() ?? DEFAULT_THEME, false);

// How much of the screen picture shows through: the app passes it in the address (only its own window has this).
const see = Number(/(?:^|[#&])see=([0-9.]+)/.exec(location.hash)?.[1]);
const sounds = new Sounds(!/(?:^|[#&])sound=0/.test(location.hash));
const seeThrough = Number.isFinite(see) && see > 0 && see <= 0.7 ? see : 0;

const root = document.getElementById("couch") as HTMLElement;

function hideWindow(): void {
  if (host) host.postMessage(JSON.stringify({ type: "hide" }));
}

/** A browser tab (testing) has no window to hide, so just say so. */
const GAME_HOST = host !== undefined;

const LAST_SECTION_KEY = "pc-control.couch.section";

function loadLastSection(): string | null {
  try {
    return localStorage.getItem(LAST_SECTION_KEY);
  } catch {
    return null;
  }
}

function saveLastSection(id: string): void {
  try {
    localStorage.setItem(LAST_SECTION_KEY, id);
  } catch {
    // Not remembered; the next open starts on the first screen.
  }
}

async function ensureSession(): Promise<boolean> {
  try {
    await api.session();
    return true;
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401) return false;
  }

  // First run of this window: the app hands it a one-time key in the address.
  const key = /(?:^|[#&])local=([A-Za-z0-9_-]{20,})/.exec(location.hash)?.[1];
  history.replaceState(null, "", location.pathname + location.search);
  if (!key) return false;
  try {
    await api.pairLocal(key);
    return true;
  } catch {
    return false;
  }
}

async function start(): Promise<void> {
  if (!(await ensureSession())) {
    root.replaceChildren(el("p", "couch-fatal", "Open this screen from PC Control on the PC."));
    return;
  }

  let capabilities: Capabilities = {};
  try {
    capabilities = await api.capabilities();
  } catch {
    // Show what we can; the PC refuses the rest.
  }
  void api.theme().then((t) => applyTheme({ accent: t.accent, dark: t.dark })).catch(() => undefined);

  const link = new Link({ onState: () => undefined });
  const input = new InputChannel(link);
  link.connect();

  // ---- frame ---------------------------------------------------------------------------------
  // The screen as it was when this opened (a game, say), blurred, with the UI over it.
  const screenPicture = el("div", "screen-picture");
  if (seeThrough > 0) {
    screenPicture.style.setProperty("background-image", `url("/api/couch/backdrop?t=${Date.now()}")`);
    screenPicture.style.setProperty("opacity", String(seeThrough));
  }
  const backdrop = el("div", "backdrop");
  const tabs = el("nav", "tabs");
  const clock = el("div", "clock");
  const stage = el("main", "stage");
  const footer = el("footer", "hints");
  const toastBox = el("div", "toast");
  toastBox.hidden = true;
  const frame = el("div", "couch");
  frame.append(screenPicture, backdrop, el("div", "footer-fade"), el("header", "topbar"), stage, footer, toastBox);
  frame.querySelector(".topbar")!.append(tabs, clock);
  root.replaceChildren(frame);

  let toastTimer = 0;
  const shell: Shell = {
    toast: (text) => {
      toastBox.textContent = text;
      toastBox.hidden = false;
      window.clearTimeout(toastTimer);
      toastTimer = window.setTimeout(() => (toastBox.hidden = true), 4000);
    },
    hide: () => {
      if (GAME_HOST) hideWindow();
      else shell.toast("(In a browser the menu would close here.)");
    },
    backdrop: (url) => {
      // Fade to the new picture; a missing one just leaves the plain background.
      backdrop.style.setProperty("background-image", url ? `url("${url}")` : "none");
      backdrop.classList.toggle("on", url !== null);
    },
    pressKey: (code) => input.key(code, "press"),
    refresh: () => void showSection(sections.indexOf(current!), focused ? focusIndex() : 0),
    focus: (item) => setFocus(item),
  };

  const sections: Section[] = [];
  if (capabilities["gameLauncher"] !== false) sections.push(gamesSection(shell));
  if (capabilities["appLauncher"] !== false) sections.push(appsSection(shell));
  if (capabilities["media"] !== false) sections.push(mediaSection(shell));
  if (capabilities["processKiller"] === true) sections.push(runningSection(shell));
  const power = capabilities["power"] === true;
  const dpi = capabilities["dpiToggle"] === true;
  if (capabilities["performance"] !== false) sections.push(performanceSection(shell));
  sections.push(pcSection(shell, { power, dpi }));

  const tabButtons = sections.map((s) => {
    const t = el("div", "tab");
    t.append(icon(s.icon), el("span", "", s.title));
    tabs.append(t);
    stage.append(s.element);
    return t;
  });

  // ---- focus ---------------------------------------------------------------------------------
  let current: Section | null = null;
  let focused: HTMLElement | null = null;

  const items = (): HTMLElement[] => (current ? [...current.element.querySelectorAll<HTMLElement>("[data-focusable]")] : []);
  const focusIndex = (): number => Math.max(0, items().indexOf(focused!));

  /** The nearest ancestor that scrolls: the screen itself, or a list inside it. */
  const scrollerOf = (item: HTMLElement): HTMLElement | null => {
    for (let e = item.parentElement; e && e !== frame; e = e.parentElement) {
      const overflow = getComputedStyle(e).overflowY;
      if ((overflow === "auto" || overflow === "scroll") && e.scrollHeight > e.clientHeight) return e;
    }
    return current?.element ?? null;
  };

  const setFocus = (item: HTMLElement | null): void => {
    focused?.classList.remove("focused");
    focused = item;
    if (item) {
      item.classList.add("focused");
      const container = scrollerOf(item);
      if (items()[0] === item && container) {
        // The very first item: go all the way to the top, so the screen's title is in view too.
        container.scrollTo({ top: 0, behavior: "smooth" });
      } else {
        item.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
      }
      handlersOf(item).focus?.();
    } else {
      shell.backdrop(null);
    }
    renderHints(footer, (item && handlersOf(item).hints) || current?.hints || []);
  };

  const showSection = async (index: number, keep = 0): Promise<void> => {
    const next = sections[index];
    if (!next) return;
    current?.leave?.();
    current = next;
    frame.dataset["screen"] = next.id;
    saveLastSection(next.id);
    sections.forEach((s, i) => {
      s.element.hidden = i !== index;
      tabButtons[i]!.classList.toggle("on", i === index);
    });
    shell.backdrop(null);
    const first = await next.show();
    const list = items();
    setFocus(list[Math.min(keep, list.length - 1)] ?? first);
  };

  const move = (direction: Direction): void => {
    const list = items();
    if (list.length === 0) return;
    if (!focused || !list.includes(focused)) {
      setFocus(list[0]!);
      return;
    }
    const boxes = list.map((e, id) => ({ id, box: (({ x, y, width, height }) => ({ x, y, width, height }))(e.getBoundingClientRect()) }));
    const from = boxes[list.indexOf(focused)]!.box;
    const target = pickNext(from, boxes.filter((b) => list[b.id] !== focused), direction);
    if (target !== null) {
      setFocus(list[target]!);
      sounds.play("move");
    } else {
      sounds.play("bump"); // the edge: nothing further that way
    }
  };

  const steps = new StepLimiter();
  const onAction = (action: PadAction): void => {
    switch (action) {
      case "up":
      case "down":
      case "left":
      case "right":
        if (steps.allow(action, performance.now())) move(action);
        break;
      case "accept": {
        const accept = handlersOf(focused).accept;
        if (accept) {
          sounds.play("accept");
          accept();
        }
        break;
      }
      case "x":
        handlersOf(focused).x?.();
        break;
      case "y":
        handlersOf(focused).y?.();
        break;
      case "prev":
      case "next": {
        const at = sections.indexOf(current!);
        const to = (at + (action === "next" ? 1 : -1) + sections.length) % sections.length;
        sounds.play("tab");
        void showSection(to);
        break;
      }
      case "menu":
        current?.menu?.();
        break;
      case "back":
        sounds.play("back");
        shell.hide();
        break;
      default:
        break;
    }
  };

  // ---- input ---------------------------------------------------------------------------------
  const pad = new PadReader(onAction);
  // Once a controller has been seen, the keyboard is ignored. Steam's controller settings can turn
  // controller buttons into key presses as well, which made every press count twice (two tiles
  // moved, or Start also acting as Enter and launching the app). The keyboard stays for trying the
  // screen without a controller.
  let controllerSeen = false;
  const poll = (now: number): void => {
    const pads = navigator.getGamepads?.() ?? [];
    const first = pads.find((p): p is Gamepad => p !== null && p.connected);
    if (first) controllerSeen = true;
    pad.update(first ? { buttons: first.buttons.map((b) => b.pressed), axes: first.axes } : null, now);
    requestAnimationFrame(poll);
  };
  requestAnimationFrame(poll);

  document.addEventListener("keydown", (e) => {
    if (controllerSeen) return;
    const action = actionForKey(e.key);
    if (action === null) return;
    e.preventDefault();
    onAction(action);
  });

  // ---- clock and visibility ------------------------------------------------------------------
  const tick = () => (clock.textContent = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }));
  tick();
  window.setInterval(tick, 15_000);

  // Each time the window is shown again, read everything fresh.
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") void showSection(Math.max(0, sections.indexOf(current!)));
  });

  // Open on the screen that was open last time.
  const remembered = sections.findIndex((s) => s.id === loadLastSection());
  void showSection(Math.max(0, remembered));
}

void start();

export type { Hint };
