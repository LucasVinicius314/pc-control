// The remote-control screen: trackpad, click buttons, keyboard, special keys and settings.

import { api, type Capabilities, type Session } from "./api";
import { GestureRecognizer } from "./gestures";
import { InputChannel } from "./input";
import { KeyboardInput } from "./keyboard";
import { MODIFIER_CODES, Modifiers } from "./modifiers";
import { Link, type LinkState } from "./socket";
import { loadPrefs, ViewClient } from "./views";
import { DISPLAY_FPS, MINI_ZOOMS, nextCorner, type ViewPrefs } from "./viewlogic";

const TICK_MS = 50;

const STATUS: Record<LinkState, [string, string]> = {
  connecting: ["warn", "Connecting…"],
  connected: ["ok", "Connected"],
  paused: ["warn", "Paused on the PC"],
  reconnecting: ["warn", "Reconnecting…"],
  unauthorized: ["bad", "Not paired anymore"],
};

const SENSITIVITY_KEY = "pc-control.sensitivity";
const NATURAL_SCROLL_KEY = "pc-control.naturalScroll";

function load(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null; // private windows and blocked storage must not break the remote
  }
}

function save(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Settings simply will not be remembered.
  }
}

function button(label: string, className = "", onClick?: () => void): HTMLButtonElement {
  const b = document.createElement("button");
  b.type = "button";
  b.textContent = label;
  if (className) b.className = className;
  // Keep focus where it is, so tapping a key does not close the on-screen keyboard.
  b.addEventListener("pointerdown", (e) => e.preventDefault());
  if (onClick) b.addEventListener("click", onClick);
  return b;
}

export interface RemoteHandlers {
  /** The phone is no longer paired (removed on the PC, or the user chose "Forget this PC"). */
  onUnpaired: (notice: string) => void;
}

export function showRemote(
  root: HTMLElement,
  session: Session,
  capabilities: Capabilities,
  handlers: RemoteHandlers,
): void {
  // ---- state -----------------------------------------------------------------------------
  let sensitivity = Number(load(SENSITIVITY_KEY)) || 1;
  sensitivity = Math.min(3, Math.max(0.5, sensitivity));
  let natural = load(NATURAL_SCROLL_KEY) !== "false";
  let paused = false;

  // ---- connection and input ----------------------------------------------------------------
  const dot = document.createElement("span");
  dot.className = "dot";
  const statusText = document.createElement("span");
  const hint = document.createElement("p");
  hint.className = "pad-hint";

  const link: Link = new Link({
    onState: (state) => {
      const [kind, text] = STATUS[state];
      dot.className = "dot " + kind;
      statusText.textContent = text;
      paused = state === "paused";
      pad.classList.toggle("paused", paused);
      hint.textContent = paused ? "Remote control is paused on the PC." : "Touch here to move the mouse";

      if (state === "connected" || state === "paused") {
        input.setSensitivity(sensitivity);
        views.setOnline(true);
      } else {
        views.setOnline(false);
        // The PC released everything when the socket closed; make the phone agree.
        input.reset();
        mods.forget();
        gestures.pointerCancel();
      }

      if (state === "unauthorized") {
        link.close();
        handlers.onUnpaired("This phone was removed on the PC. Pair again.");
      }
    },
    onServerError: (code) => console.debug("PC refused:", code),
    onBinary: (data) => views.handleBinary(data),
    onViewStatus: (stream, state) => views.handleStatus(stream, state),
  });

  const input = new InputChannel(link);
  const mods = new Modifiers(
    (code, down) => input.key(code, down ? "down" : "up"),
    () => updateModifierButtons(),
  );
  const gestures = new GestureRecognizer(input, { naturalScroll: natural });
  const keyboard = new KeyboardInput(input, mods, (focused) => keyboardButton.classList.toggle("on", focused));

  // ---- trackpad surface ----------------------------------------------------------------------
  const pad = document.createElement("div");
  pad.className = "pad";
  const displayCanvas = document.createElement("canvas");
  displayCanvas.className = "preview-display";
  displayCanvas.hidden = true;
  const miniCanvas = document.createElement("canvas");
  miniCanvas.className = "preview-mini";
  miniCanvas.hidden = true;
  const note = document.createElement("p");
  note.className = "view-note";
  note.hidden = true;
  const ring = document.createElement("div");
  ring.className = "cursor-ring";
  ring.hidden = true;
  pad.append(displayCanvas, ring, miniCanvas, hint, note);

  const views = new ViewClient(
    (message) => void link.send(JSON.stringify(message)),
    { pad, display: displayCanvas, mini: miniCanvas, ring, note },
    loadPrefs(),
  );
  pad.addEventListener("contextmenu", (e) => e.preventDefault());

  let tickTimer = 0;
  const startTicking = () => {
    if (tickTimer === 0) tickTimer = window.setInterval(() => gestures.tick(performance.now()), TICK_MS);
  };
  const stopTicking = () => {
    window.clearInterval(tickTimer);
    tickTimer = 0;
  };

  const sample = (e: PointerEvent) => ({ id: e.pointerId, x: e.clientX, y: e.clientY, t: e.timeStamp });

  pad.addEventListener("pointerdown", (e) => {
    if (paused) return;
    pad.setPointerCapture(e.pointerId);
    gestures.pointerDown(sample(e));
    startTicking();
  });
  pad.addEventListener("pointermove", (e) => gestures.pointerMove(sample(e)));
  const lift = (e: PointerEvent) => {
    gestures.pointerUp(sample(e));
    mods.consumeOnce(); // Ctrl + tap and similar: the modifier applies to this one click
    if (!gestures.active) stopTicking();
  };
  pad.addEventListener("pointerup", lift);
  pad.addEventListener("pointercancel", () => {
    gestures.pointerCancel();
    stopTicking();
  });

  // ---- toolbar -----------------------------------------------------------------------------
  const keyboardButton = button("Keyboard", "tool", () => (keyboard.focused ? keyboard.hide() : keyboard.show()));
  const leftButton = button("Left click", "tool", () => {
    input.click("left");
    mods.consumeOnce();
  });
  const rightButton = button("Right click", "tool", () => {
    input.click("right");
    mods.consumeOnce();
  });
  const keysButton = button("Keys", "tool", () => {
    const opening = drawer.hidden !== false;
    drawer.hidden = !opening;
    keysButton.classList.toggle("on", opening);
  });
  const viewButton = button("View", "tool", () => {
    const opening = viewSheet.hidden !== false;
    viewSheet.hidden = !opening;
    viewButton.classList.toggle("on", opening);
    if (opening) void loadMonitors();
  });
  const settingsButton = button("Settings", "tool", () => (settings.hidden = !settings.hidden));

  const toolbar = document.createElement("nav");
  toolbar.className = "toolbar";
  toolbar.setAttribute("aria-label", "Remote controls");
  if (capabilities["keyboard"] !== false) toolbar.append(keyboardButton);
  toolbar.append(leftButton, rightButton);
  if (capabilities["keyboard"] !== false) toolbar.append(keysButton);
  if (capabilities["magnifier"] !== false || capabilities["screenshot"] !== false) toolbar.append(viewButton);
  toolbar.append(settingsButton);

  // ---- special keys ----------------------------------------------------------------------------
  const modifierButtons = new Map<string, HTMLButtonElement>();
  const drawer = document.createElement("div");
  drawer.className = "drawer";
  drawer.hidden = true;

  const row = (...items: HTMLElement[]) => {
    const r = document.createElement("div");
    r.className = "keyrow";
    r.append(...items);
    return r;
  };
  const key = (label: string, code: string) => button(label, "key", () => keyboard.pressKey(code));

  const modifier = (label: string, code: string) => {
    const b = button(label, "key mod", () => mods.toggle(code));
    modifierButtons.set(code, b);
    return b;
  };
  const updateModifierButtons = () => {
    for (const [code, b] of modifierButtons) {
      const state = mods.state(code);
      b.classList.toggle("once", state === "once");
      b.classList.toggle("locked", state === "locked");
      b.setAttribute("aria-pressed", String(state !== "off"));
    }
  };

  const fKeys = Array.from({ length: 12 }, (_, i) => key(`F${i + 1}`, `F${i + 1}`));
  drawer.append(
    row(
      modifier("Ctrl", MODIFIER_CODES.ctrl),
      modifier("Alt", MODIFIER_CODES.alt),
      modifier("Shift", MODIFIER_CODES.shift),
      modifier("Win", MODIFIER_CODES.win),
    ),
    row(key("Esc", "Escape"), key("Tab", "Tab"), key("Enter", "Enter"), key("⌫", "Backspace"), key("Del", "Delete")),
    row(key("←", "ArrowLeft"), key("↑", "ArrowUp"), key("↓", "ArrowDown"), key("→", "ArrowRight")),
    row(key("Home", "Home"), key("End", "End"), key("PgUp", "PageUp"), key("PgDn", "PageDown")),
    row(...fKeys.slice(0, 6)),
    row(...fKeys.slice(6)),
  );

  // ---- settings --------------------------------------------------------------------------------
  const sensitivityInput = document.createElement("input");
  sensitivityInput.type = "range";
  sensitivityInput.min = "0.5";
  sensitivityInput.max = "3";
  sensitivityInput.step = "0.1";
  sensitivityInput.value = String(sensitivity);
  const sensitivityValue = document.createElement("output");
  sensitivityValue.textContent = sensitivity.toFixed(1) + "×";
  sensitivityInput.addEventListener("input", () => {
    sensitivity = Number(sensitivityInput.value);
    sensitivityValue.textContent = sensitivity.toFixed(1) + "×";
    save(SENSITIVITY_KEY, String(sensitivity));
    input.setSensitivity(sensitivity);
  });

  const naturalInput = document.createElement("input");
  naturalInput.type = "checkbox";
  naturalInput.checked = natural;
  naturalInput.addEventListener("change", () => {
    natural = naturalInput.checked;
    gestures.options = { naturalScroll: natural };
    save(NATURAL_SCROLL_KEY, String(natural));
  });

  const label = (text: string, ...controls: HTMLElement[]) => {
    const l = document.createElement("label");
    l.className = "setting";
    l.append(text, ...controls);
    return l;
  };

  const detail = document.createElement("p");
  detail.className = "meta";
  detail.textContent = session.deviceName;

  const forget = button("Forget this PC", "secondary", async () => {
    link.close();
    try {
      await api.unpair();
    } finally {
      handlers.onUnpaired("");
    }
  });

  const settings = document.createElement("section");
  settings.className = "settings card";
  settings.hidden = true;
  settings.append(
    label("Cursor speed", sensitivityInput, sensitivityValue),
    label("Natural scrolling", naturalInput),
    detail,
    forget,
  );

  // ---- previews ----------------------------------------------------------------------------------
  const update = (changes: Partial<ViewPrefs>) => {
    views.setPrefs({ ...views.prefs, ...changes });
    refreshViewSheet();
  };

  const displayToggle = document.createElement("input");
  displayToggle.type = "checkbox";
  displayToggle.addEventListener("change", () => update({ display: displayToggle.checked }));

  const miniToggle = document.createElement("input");
  miniToggle.type = "checkbox";
  miniToggle.addEventListener("change", () => update({ mini: miniToggle.checked }));

  const fpsLabels: Record<number, string> = { 3: "Low", 6: "Medium", 10: "High" };
  const fpsButtons = DISPLAY_FPS.map((fps) => button(fpsLabels[fps] ?? String(fps), "key", () => update({ displayFps: fps })));
  const zoomButtons = MINI_ZOOMS.map((zoom) => button(`${zoom}×`, "key", () => update({ miniZoom: zoom })));
  const moveButton = button("Move", "key", () => update({ corner: nextCorner(views.prefs.corner) }));

  const monitorSelect = document.createElement("select");
  monitorSelect.addEventListener("change", () => update({ monitor: Number(monitorSelect.value) }));
  const monitorOption = (value: number, text: string) => {
    const o = document.createElement("option");
    o.value = String(value);
    o.textContent = text;
    return o;
  };
  monitorSelect.append(monitorOption(-1, "Follow the cursor"));

  let monitorsLoaded = false;
  const loadMonitors = async () => {
    if (monitorsLoaded) return;
    try {
      const monitors = await api.monitors();
      monitorSelect.replaceChildren(
        monitorOption(-1, "Follow the cursor"),
        ...monitors.map((m) =>
          monitorOption(m.index, `Monitor ${m.index + 1}${m.primary ? " (main)" : ""} · ${m.width}×${m.height}`),
        ),
      );
      monitorsLoaded = true;
      refreshViewSheet();
    } catch {
      // The picker stays on "Follow the cursor".
    }
  };

  const refreshViewSheet = () => {
    const p = views.prefs;
    displayToggle.checked = p.display;
    miniToggle.checked = p.mini;
    monitorSelect.value = String(p.monitor);
    zoomButtons.forEach((b, i) => b.classList.toggle("locked", MINI_ZOOMS[i] === p.miniZoom));
    fpsButtons.forEach((b, i) => b.classList.toggle("locked", DISPLAY_FPS[i] === p.displayFps));
  };

  const viewSheet = document.createElement("section");
  viewSheet.className = "settings card";
  viewSheet.hidden = true;
  const zoomRow = document.createElement("div");
  zoomRow.className = "keyrow";
  zoomRow.append(...zoomButtons, moveButton);
  const fpsRow = document.createElement("div");
  fpsRow.className = "keyrow";
  fpsRow.append(...fpsButtons);
  viewSheet.append(
    label("Display preview", displayToggle),
    fpsRow,
    label("Mini preview around the cursor", miniToggle),
    zoomRow,
    label("Screen", monitorSelect),
  );
  refreshViewSheet();

  // ---- assemble ----------------------------------------------------------------------------------
  const header = document.createElement("header");
  header.className = "bar";
  const status = document.createElement("div");
  status.className = "status";
  status.append(dot, statusText);
  header.append(status);

  const screen = document.createElement("div");
  screen.className = "remote";
  screen.append(header, pad, viewSheet, settings, drawer, toolbar, keyboard.element);
  root.replaceChildren(screen);
  root.classList.add("fill");

  link.connect();
}
