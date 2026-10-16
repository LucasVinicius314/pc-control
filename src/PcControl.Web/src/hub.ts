// A panel with a row of tabs on top, for features that belong together (the launcher: apps, running
// programs, open a link). The page shows one tab at a time and remembers which one was last used.

export interface HubTab {
  id: string;
  label: string;
  element: HTMLElement;
  /** Called each time the tab is shown, for example to read the list again. */
  onShow?: () => void | Promise<void>;
}

export interface Hub {
  element: HTMLElement;
  /** Shows the remembered tab (or the first one). Call each time the panel is opened. */
  open: () => Promise<void>;
}

/** Which tab to show: the remembered one if it still exists, otherwise the first. No DOM here. */
export function pickTab(ids: readonly string[], remembered: string | null): string | null {
  if (remembered !== null && ids.includes(remembered)) return remembered;
  return ids[0] ?? null;
}

export function createHub(tabs: readonly HubTab[]): Hub {
  const element = document.createElement("section");
  element.className = "hub";
  element.hidden = true;

  const bar = document.createElement("div");
  bar.className = "segmented";
  bar.setAttribute("role", "tablist");

  const buttons = new Map<string, HTMLButtonElement>();
  let current: string | null = null;

  const show = async (id: string) => {
    current = id;
    for (const tab of tabs) {
      const active = tab.id === id;
      tab.element.hidden = !active;
      const b = buttons.get(tab.id)!;
      b.classList.toggle("on", active);
      b.setAttribute("aria-selected", String(active));
    }
    await tabs.find((t) => t.id === id)?.onShow?.();
  };

  for (const tab of tabs) {
    const b = document.createElement("button");
    b.type = "button";
    b.textContent = tab.label;
    b.setAttribute("role", "tab");
    // Keep focus where it is, so switching tab does not close the on-screen keyboard.
    b.addEventListener("pointerdown", (e) => e.preventDefault());
    b.addEventListener("click", () => void show(tab.id));
    buttons.set(tab.id, b);
    bar.append(b);
    tab.element.hidden = true;
  }

  // One tab needs no tab bar.
  if (tabs.length > 1) element.append(bar);
  element.append(...tabs.map((t) => t.element));

  return {
    element,
    open: async () => {
      const id = pickTab(tabs.map((t) => t.id), current);
      if (id !== null) await show(id);
    },
  };
}
