// The building blocks of the launcher tabs and the PC panel: open a link, running programs (switch to
// or close), and power buttons. Each is made on its own with the features it should show.

import { api, type ProcessItem } from "./api";
import { icon, type IconName } from "./icons";
import {
  ConfirmGate,
  countdownText,
  needsConfirmation,
  powerDone,
  powerMessage,
  processMessage,
  urlMessage,
  type PowerName,
} from "./systemview";

export interface SystemPanelHandlers {
  onMessage: (message: string) => void;
}

export interface SystemPanelFeatures {
  url: boolean;
  power: boolean;
  processes: boolean;
  /** Show a small title above each section. Off when the panel sits under a tab that already says what it is. */
  titles?: boolean;
}

export interface SystemPanel {
  element: HTMLElement;
  /** Call each time the panel is opened: reads the running programs and any countdown again. */
  open: () => Promise<void>;
}

export const POWER_BUTTONS: { action: PowerName; label: string; hint: string; icon: IconName }[] = [
  { action: "lock", label: "Lock", hint: "Lock the PC", icon: "lock" },
  { action: "sleep", label: "Sleep", hint: "Put the PC to sleep", icon: "sleep" },
  { action: "signOut", label: "Sign out", hint: "Sign out of Windows (tap twice)", icon: "signOut" },
  { action: "restart", label: "Restart", hint: "Restart the PC in 15 seconds (tap twice)", icon: "restart" },
  { action: "shutdown", label: "Shut down", hint: "Shut the PC down in 15 seconds (tap twice)", icon: "power" },
];

/** A button with an icon, a short label, and the same words as a tooltip and for screen readers. */
export function tile(className: string, iconName: IconName, label: string, hint: string): { button: HTMLButtonElement; label: HTMLElement } {
  const b = document.createElement("button");
  b.type = "button";
  b.className = className;
  b.title = hint;
  b.setAttribute("aria-label", hint);
  const text = document.createElement("span");
  text.textContent = label;
  // An icon-only button gets no (empty) label element: it would take up a grid cell and push the icon off centre.
  if (label === "") b.append(icon(iconName));
  else b.append(icon(iconName), text);
  return { button: b, label: text };
}

export function heading(text: string): HTMLElement {
  const h = document.createElement("h2");
  h.className = "sys-title";
  h.textContent = text;
  return h;
}

export function createSystemPanel(handlers: SystemPanelHandlers, features: SystemPanelFeatures): SystemPanel {
  const panel = document.createElement("section");
  panel.className = features.titles ? "system-panel card" : "hub-pane";
  panel.hidden = true;
  const titled = (text: string): HTMLElement[] => (features.titles ? [heading(text)] : []);

  let countdownTimer = 0;
  let refreshProcesses: () => Promise<void> = async () => {};
  let refreshPending: () => Promise<void> = async () => {};

  // ---- open a link ------------------------------------------------------------------------------
  if (features.url) {
    const input = document.createElement("input");
    input.type = "url";
    input.placeholder = "Web address to open on the PC";
    input.autocomplete = "off";
    input.setAttribute("autocapitalize", "off");
    input.setAttribute("autocorrect", "off");
    input.setAttribute("enterkeyhint", "go");
    input.setAttribute("aria-label", "Web address to open on the PC");
    input.spellcheck = false;

    const { button: open } = tile("sys-go", "link", "Open", "Open this address in the PC's browser");

    const send = async () => {
      const url = input.value.trim();
      if (url === "") return;
      try {
        await api.openUrl(url);
        input.value = "";
        handlers.onMessage("Opened on the PC.");
      } catch (error) {
        handlers.onMessage(urlMessage(error));
      }
    };
    open.addEventListener("click", () => void send());
    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") void send();
    });

    const row = document.createElement("div");
    row.className = "sys-row";
    row.append(input, open);
    panel.append(...titled("Open a link"), row);
  }

  // ---- power ------------------------------------------------------------------------------------
  if (features.power) {
    const gate = new ConfirmGate();
    const buttons = new Map<PowerName, { button: HTMLButtonElement; label: HTMLElement }>();

    const banner = document.createElement("div");
    banner.className = "sys-banner";
    banner.hidden = true;
    const bannerText = document.createElement("span");
    const { button: cancel } = tile("sys-cancel", "closeX", "Cancel", "Cancel the restart or shutdown");
    banner.append(bannerText, cancel);

    const showCountdown = (action: string, secondsLeft: number) => {
      window.clearInterval(countdownTimer);
      let left = secondsLeft;
      bannerText.textContent = countdownText(action, left);
      banner.hidden = false;
      countdownTimer = window.setInterval(() => {
        left -= 1;
        if (left <= 0) {
          window.clearInterval(countdownTimer);
          banner.hidden = true;
          return;
        }
        bannerText.textContent = countdownText(action, left);
      }, 1000);
    };

    const disarmAll = () => {
      gate.reset();
      for (const [action, b] of buttons) {
        b.button.classList.remove("armed");
        b.label.textContent = POWER_BUTTONS.find((p) => p.action === action)!.label;
      }
    };

    const run = async (action: PowerName) => {
      try {
        const result = await api.power(action, needsConfirmation(action));
        handlers.onMessage(powerDone(action, result.delaySeconds));
        if (result.delaySeconds > 0) showCountdown(action, result.delaySeconds);
      } catch (error) {
        handlers.onMessage(powerMessage(error));
      }
    };

    const row = document.createElement("div");
    row.className = "sys-power";
    for (const { action, label, hint, icon: iconName } of POWER_BUTTONS) {
      const t = tile("tile", iconName, label, hint);
      const b = t.button;
      if (needsConfirmation(action)) b.classList.add("danger");
      b.addEventListener("click", () => {
        if (!needsConfirmation(action)) {
          disarmAll();
          void run(action);
          return;
        }
        if (gate.tap(action)) {
          disarmAll();
          void run(action);
          return;
        }
        // First tap: ask for a second one, on this button only.
        for (const [other, ob] of buttons) {
          ob.button.classList.toggle("armed", other === action);
          ob.label.textContent = other === action ? "Tap again" : POWER_BUTTONS.find((p) => p.action === other)!.label;
        }
        window.setTimeout(() => {
          if (!gate.isArmed(action)) disarmAll();
        }, 4100);
      });
      buttons.set(action, t);
      row.append(b);
    }

    cancel.addEventListener("click", async () => {
      try {
        await api.cancelPower();
        window.clearInterval(countdownTimer);
        banner.hidden = true;
        handlers.onMessage("Cancelled.");
      } catch (error) {
        handlers.onMessage(powerMessage(error));
        await refreshPending();
      }
    });

    refreshPending = async () => {
      try {
        const { pending } = await api.powerStatus();
        if (pending) {
          showCountdown(pending.action, pending.secondsLeft);
        } else {
          window.clearInterval(countdownTimer);
          banner.hidden = true;
        }
      } catch {
        // The status is only a convenience; the buttons still work.
      }
    };

    panel.append(...titled("Power"), row, banner);
  }

  // ---- programs ---------------------------------------------------------------------------------
  if (features.processes) {
    const list = document.createElement("div");
    list.className = "sys-processes";

    const empty = document.createElement("p");
    empty.className = "sys-empty";
    empty.textContent = "No programs with a window are open.";

    const focus = async (item: ProcessItem) => {
      try {
        await api.focusProcess(item.id);
        handlers.onMessage(`Switched to ${item.title}.`);
      } catch (error) {
        handlers.onMessage(processMessage(error));
        void refreshProcesses();
      }
    };

    const close = async (item: ProcessItem, force: boolean) => {
      try {
        await api.closeProcess(item.id, force);
        handlers.onMessage(force ? `Force closed ${item.title}.` : `Asked ${item.title} to close.`);
      } catch (error) {
        handlers.onMessage(processMessage(error));
      }
      // Programs take a moment to go away; look again shortly.
      window.setTimeout(() => void refreshProcesses(), 700);
    };

    const render = (items: ProcessItem[]) => {
      if (items.length === 0) {
        list.replaceChildren(empty);
        return;
      }
      list.replaceChildren(
        ...items.map((item) => {
          const row = document.createElement("div");
          row.className = "sys-process";
          const text = document.createElement("div");
          text.className = "sys-process-text";
          const name = document.createElement("span");
          name.className = "sys-process-title";
          name.textContent = item.title;
          name.title = item.title;
          const program = document.createElement("span");
          program.className = "sys-process-name";
          program.textContent = item.name;
          text.append(name, program);

          const show = tile("round", "window", "", `Switch to ${item.title}`).button;
          show.addEventListener("click", () => void focus(item));
          const gentle = tile("round", "closeX", "", `Close ${item.title}`).button;
          gentle.addEventListener("click", () => void close(item, false));
          const force = tile("round danger", "force", "", `Force close ${item.title}: ends it now, unsaved work is lost`).button;
          force.addEventListener("click", () => void close(item, true));
          row.append(text, show, gentle, force);
          return row;
        }),
      );
    };

    refreshProcesses = async () => {
      try {
        render(await api.processes());
      } catch (error) {
        handlers.onMessage(processMessage(error));
      }
    };

    panel.append(...titled("Switch to or close a program"), list);
  }

  return {
    element: panel,
    open: async () => {
      await Promise.all([refreshPending(), refreshProcesses()]);
    },
  };
}
