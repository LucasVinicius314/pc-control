// The system panel: open a link on the PC, power buttons, and a list of programs that can be closed.

import { api, type ProcessItem } from "./api";
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
}

export interface SystemPanel {
  element: HTMLElement;
  /** Call each time the panel is opened: reads the running programs and any countdown again. */
  open: () => Promise<void>;
}

const POWER_BUTTONS: { action: PowerName; label: string; hint: string }[] = [
  { action: "lock", label: "Lock", hint: "Lock the PC" },
  { action: "sleep", label: "Sleep", hint: "Put the PC to sleep" },
  { action: "signOut", label: "Sign out", hint: "Sign out of Windows" },
  { action: "restart", label: "Restart", hint: "Restart the PC" },
  { action: "shutdown", label: "Shut down", hint: "Shut the PC down" },
];

function heading(text: string): HTMLElement {
  const h = document.createElement("h2");
  h.className = "sys-title";
  h.textContent = text;
  return h;
}

export function createSystemPanel(handlers: SystemPanelHandlers, features: SystemPanelFeatures): SystemPanel {
  const panel = document.createElement("section");
  panel.className = "system-panel card";
  panel.hidden = true;

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

    const open = document.createElement("button");
    open.type = "button";
    open.textContent = "Open";

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
    panel.append(heading("Open a link on the PC"), row);
  }

  // ---- power ------------------------------------------------------------------------------------
  if (features.power) {
    const gate = new ConfirmGate();
    const buttons = new Map<PowerName, HTMLButtonElement>();

    const banner = document.createElement("div");
    banner.className = "sys-banner";
    banner.hidden = true;
    const bannerText = document.createElement("span");
    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = "Cancel";
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
        b.classList.remove("armed");
        b.textContent = POWER_BUTTONS.find((p) => p.action === action)!.label;
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
    for (const { action, label, hint } of POWER_BUTTONS) {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      b.title = hint;
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
          ob.classList.toggle("armed", other === action);
          ob.textContent = other === action ? "Tap again to confirm" : POWER_BUTTONS.find((p) => p.action === other)!.label;
        }
        window.setTimeout(() => {
          if (!gate.isArmed(action)) disarmAll();
        }, 4100);
      });
      buttons.set(action, b);
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

    panel.append(heading("Power"), row, banner);
  }

  // ---- programs ---------------------------------------------------------------------------------
  if (features.processes) {
    const list = document.createElement("div");
    list.className = "sys-processes";

    const empty = document.createElement("p");
    empty.className = "sys-empty";
    empty.textContent = "No programs with a window are open.";

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
          const name = document.createElement("span");
          name.className = "sys-process-title";
          name.textContent = item.title;
          name.title = item.name;
          const gentle = document.createElement("button");
          gentle.type = "button";
          gentle.textContent = "Close";
          gentle.addEventListener("click", () => void close(item, false));
          const force = document.createElement("button");
          force.type = "button";
          force.className = "danger";
          force.textContent = "Force";
          force.title = "End it immediately; unsaved work is lost";
          force.addEventListener("click", () => void close(item, true));
          row.append(name, gentle, force);
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

    panel.append(heading("Close a program"), list);
  }

  return {
    element: panel,
    open: async () => {
      await Promise.all([refreshPending(), refreshProcesses()]);
    },
  };
}
