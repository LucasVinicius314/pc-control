// The pieces every controller-UI screen shares: items that can be focused and what the buttons do
// on them, and the footer that tells the user which button does what.

import { icon, type IconName } from "../icons";

export interface Hint {
  /** "A", "B", "X", "Y", "LB / RB". */
  button: string;
  label: string;
}

/** What the buttons do while one item has the focus. Missing means the button does nothing there. */
export interface ItemHandlers {
  accept?: () => void;
  x?: () => void;
  y?: () => void;
  /** Called when the item gets the focus, for example to show that game's background. */
  focus?: () => void;
  /** The footer while this item is focused. */
  hints?: Hint[];
}

const handlers = new WeakMap<HTMLElement, ItemHandlers>();

/** Marks an element as something the controller can land on. */
export function makeItem(element: HTMLElement, h: ItemHandlers): HTMLElement {
  element.dataset["focusable"] = "1";
  element.tabIndex = -1;
  handlers.set(element, h);
  return element;
}

export function handlersOf(element: HTMLElement | null): ItemHandlers {
  return (element && handlers.get(element)) || {};
}

export function el<K extends keyof HTMLElementTagNameMap>(tag: K, className = "", text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

/** A square-ish tile with an icon over a label, the shape used for media keys and power. */
export function tile(iconName: IconName, label: string, className = ""): HTMLElement {
  const t = el("div", `tile ${className}`.trim());
  t.append(icon(iconName), el("span", "", label));
  return t;
}

export function heading(text: string): HTMLElement {
  return el("h2", "shelf-title", text);
}

/** A row or grid of items. */
export function shelf(...children: HTMLElement[]): HTMLElement {
  const s = el("div", "shelf");
  s.append(...children);
  return s;
}

/** Puts an image into a card; a card without a picture shows the first letter instead. */
export function cover(src: string, fallbackText: string, className = "cover", fill = false): HTMLElement {
  const box = el("div", className);
  const letter = el("span", "cover-letter", fallbackText.trim().charAt(0).toUpperCase());
  const img = el("img");
  img.alt = "";
  img.loading = "lazy";
  img.draggable = false;
  img.addEventListener("load", () => {
    letter.remove();
    // The picture itself, blurred, fills what its shape leaves empty (tall box art in a wide frame).
    if (fill) box.style.setProperty("--art", `url("${src}")`);
  });
  img.addEventListener("error", () => img.remove());
  img.src = src;
  box.append(letter, img);
  return box;
}

export function renderHints(footer: HTMLElement, hints: readonly Hint[]): void {
  footer.replaceChildren(
    ...hints.map((h) => {
      const item = el("span", "hint");
      // The letter sits in its own element so its top and bottom can be trimmed to the capital letters,
      // which centres it in the circle whatever the font's spacing is.
      const badge = el("kbd", `pad-button pad-${h.button.toLowerCase().replace(/[^a-z]/g, "")}`);
      badge.append(el("span", "pad-label", h.button));
      item.append(badge, el("span", "", h.label));
      return item;
    }),
  );
}

export function message(text: string, detail?: string): HTMLElement {
  const box = el("div", "couch-message");
  box.append(el("p", "couch-message-title", text));
  if (detail) box.append(el("p", "couch-message-detail", detail));
  return box;
}
