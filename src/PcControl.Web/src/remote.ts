// The remote-control screen: trackpad, click buttons, keyboard, special keys and settings.

import { api, type Capabilities, type Session } from "./api";
import { GestureRecognizer } from "./gestures";
import { InputChannel } from "./input";
import { KeyboardInput } from "./keyboard";
import { MODIFIER_CODES, Modifiers } from "./modifiers";
import { Link, type LinkState } from "./socket";
import { loadPrefs, ViewClient } from "./views";
import { createAppsPanel } from "./apps";
import { createSystemPanel } from "./system";
import { badgeText, changedMessage, failureMessage } from "./dpi";
import { icon, type IconName } from "./icons";
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

/**
 * A toolbar button that shows only an icon. The label is still there for screen readers and as a
 * tooltip, so it stays clear what each one does.
 */
function iconButton(name: IconName, label: string, onClick: () => void): HTMLButtonElement {
  const b = button("", "tool icon-button", onClick);
  b.append(icon(name));
  b.setAttribute("aria-label", label);
  b.title = label;
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
  const toast = document.createElement("p");
  toast.className = "toast";
  toast.setAttribute("role", "status");
  toast.hidden = true;
  pad.append(displayCanvas, ring, miniCanvas, hint, note, toast);

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
  const keyboardButton = iconButton("keyboard", "Keyboard", () => (keyboard.focused ? keyboard.hide() : keyboard.show()));
  const leftButton = iconButton("leftClick", "Left click", () => {
    input.click("left");
    mods.consumeOnce();
  });
  const rightButton = iconButton("rightClick", "Right click", () => {
    input.click("right");
    mods.consumeOnce();
  });
  // Only one panel is open at a time. Stacked together they would take all the room and push the
  // toolbar, and with it the way to close them, off the screen.
  const panelButtons = new Map<HTMLElement, HTMLButtonElement>();
  const togglePanel = (panel: HTMLElement): boolean => {
    const opening = panel.hidden !== false;
    for (const [other, otherButton] of panelButtons) {
      other.hidden = true;
      otherButton.classList.remove("on");
    }
    if (opening) {
      panel.hidden = false;
      panelButtons.get(panel)?.classList.add("on");
    }
    // The launcher is a long list, so it takes the trackpad's place while it is open.
    screen.classList.toggle("apps-open", !appsPanel.element.hidden);
    return opening;
  };

  const appsPanel = createAppsPanel(
    {
      onLaunched: (name) => {
        togglePanel(appsPanel.element); // close the launcher so the app can be seen on the preview
        showToast(`Launching ${name}…`);
      },
      onProblem: (message) => showToast(message),
      onPowerToysOpened: (message) => {
        togglePanel(appsPanel.element);
        keyboard.show(); // PowerToys Run is waiting for typing
        showToast(message);
      },
    },
    capabilities["powerToysRun"] !== false,
  );
  const appsButton = iconButton("apps", "Apps", () => {
    if (togglePanel(appsPanel.element)) void appsPanel.open();
  });

  const keysButton = iconButton("keys", "Special keys", () => void togglePanel(drawer));
  const viewButton = iconButton("view", "Screen preview", () => {
    if (togglePanel(viewSheet)) void loadMonitors();
  });
  // The display-scale button: shows the current scale under its icon and flips it on a tap.
  const dpiBadge = document.createElement("span");
  dpiBadge.className = "badge";
  dpiBadge.textContent = badgeText(null);
  let dpiBusy = false;
  const dpiButton = iconButton("dpi", "Display scale", () => void toggleDpi());
  dpiButton.append(dpiBadge);

  const refreshDpi = async () => {
    try {
      dpiBadge.textContent = badgeText(await api.dpi());
    } catch {
      dpiBadge.textContent = badgeText(null);
    }
  };

  const toggleDpi = async () => {
    if (dpiBusy) return;
    dpiBusy = true;
    dpiButton.classList.add("busy");
    try {
      const change = await api.toggleDpi();
      dpiBadge.textContent = badgeText({ available: true, percent: change.percent });
      showToast(changedMessage(change));
    } catch (error) {
      showToast(failureMessage(error));
      await refreshDpi();
    } finally {
      dpiBusy = false;
      dpiButton.classList.remove("busy");
    }
  };

  let toastTimer = 0;
  const showToast = (text: string) => {
    toast.textContent = text;
    toast.hidden = false;
    window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(() => (toast.hidden = true), 5000);
  };

  const mediaButton = iconButton("media", "Media", () => void togglePanel(mediaPanel));
  const systemFeatures = {
    url: capabilities["sendUrl"] === true,
    power: capabilities["power"] === true,
    processes: capabilities["processKiller"] === true,
  };
  const systemPanel = createSystemPanel({ onMessage: (message) => showToast(message) }, systemFeatures);
  const systemButton = iconButton("power", "System: links, power, close programs", () => {
    if (togglePanel(systemPanel.element)) void systemPanel.open();
  });
  const settingsButton = iconButton("settings", "Settings", () => void togglePanel(settings));

  const toolbar = document.createElement("nav");
  toolbar.className = "toolbar";
  toolbar.setAttribute("aria-label", "Remote controls");
  if (capabilities["keyboard"] !== false) toolbar.append(keyboardButton);
  toolbar.append(leftButton, rightButton);
  if (capabilities["keyboard"] !== false) toolbar.append(keysButton);
  if (capabilities["appLauncher"] !== false) toolbar.append(appsButton);
  if (capabilities["magnifier"] !== false || capabilities["screenshot"] !== false) toolbar.append(viewButton);
  if (capabilities["dpiToggle"] === true) {
    toolbar.append(dpiButton);
    void refreshDpi();
    // The scale can be changed on the PC too, so look again whenever the page comes back.
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "visible") void refreshDpi();
    });
  }
  if (capabilities["media"] !== false && capabilities["keyboard"] !== false) toolbar.append(mediaButton);
  if (systemFeatures.url || systemFeatures.power || systemFeatures.processes) toolbar.append(systemButton);
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

  // ---- media -----------------------------------------------------------------------------------
  // Holding a seek or volume button keeps repeating it, like holding the key on a keyboard.
  const repeatKey = (label: string, code: string, title: string) => {
    const b = button(label, "key");
    b.title = title;
    b.setAttribute("aria-label", title);
    let delay = 0;
    let timer = 0;
    const stop = () => {
      window.clearTimeout(delay);
      window.clearInterval(timer);
    };
    b.addEventListener("pointerdown", () => {
      input.key(code, "press");
      stop();
      delay = window.setTimeout(() => (timer = window.setInterval(() => input.key(code, "press"), 120)), 400);
    });
    for (const ev of ["pointerup", "pointercancel", "pointerleave"]) b.addEventListener(ev, stop);
    return b;
  };
  const tap = (label: string, code: string, title: string) => {
    const b = button(label, "key", () => input.key(code, "press"));
    b.title = title;
    b.setAttribute("aria-label", title);
    return b;
  };

  const mediaPanel = document.createElement("div");
  mediaPanel.className = "drawer";
  mediaPanel.hidden = true;
  mediaPanel.append(
    row(
      tap("⏮", "MediaTrackPrevious", "Previous"),
      tap("⏯", "MediaPlayPause", "Play / pause"),
      tap("⏭", "MediaTrackNext", "Next"),
      tap("⛶ F", "KeyF", "Fullscreen (F)"),
    ),
    row(
      repeatKey("«10", "KeyJ", "Back 10 s (J)"),
      repeatKey("◀ 5", "ArrowLeft", "Back 5 s (left arrow)"),
      repeatKey("5 ▶", "ArrowRight", "Forward 5 s (right arrow)"),
      repeatKey("10»", "KeyL", "Forward 10 s (L)"),
    ),
    row(
      repeatKey("🔉", "AudioVolumeDown", "PC volume down"),
      tap("🔇", "AudioVolumeMute", "Mute the PC"),
      repeatKey("🔊", "AudioVolumeUp", "PC volume up"),
    ),
    row(
      repeatKey("▼ player", "ArrowDown", "Player volume down (down arrow)"),
      repeatKey("▲ player", "ArrowUp", "Player volume up (up arrow)"),
      tap("Space", "Space", "Pause in the player (Space)"),
      tap("Esc", "Escape", "Leave fullscreen (Esc)"),
    ),
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
  panelButtons.set(appsPanel.element, appsButton);
  panelButtons.set(drawer, keysButton);
  panelButtons.set(viewSheet, viewButton);
  panelButtons.set(mediaPanel, mediaButton);
  panelButtons.set(systemPanel.element, systemButton);
  panelButtons.set(settings, settingsButton);
  screen.append(header, pad, appsPanel.element, viewSheet, settings, drawer, mediaPanel, systemPanel.element, toolbar, keyboard.element);
  root.replaceChildren(screen);
  root.classList.add("fill");

  link.connect();
}
