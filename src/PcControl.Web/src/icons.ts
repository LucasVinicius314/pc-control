// The toolbar icons. They are small original line drawings on a 24 x 24 grid, drawn with the current
// text colour so they follow the button's state (normal, active, disabled). No icon library, no
// images to load: each one is a few shapes.

export type IconName = "keyboard" | "leftClick" | "rightClick" | "keys" | "view" | "dpi" | "settings";

interface Shape {
  tag: "path" | "rect" | "circle";
  attrs: Record<string, string>;
  /** Solid instead of outlined, for highlighted parts such as the pressed mouse button. */
  filled?: boolean;
}

const path = (d: string, filled = false): Shape => ({ tag: "path", attrs: { d }, filled });

export const ICONS: Record<IconName, Shape[]> = {
  // A keyboard: the body, two rows of keys and a space bar.
  keyboard: [
    { tag: "rect", attrs: { x: "2", y: "6", width: "20", height: "12", rx: "2.5" } },
    path("M6 10h.01M10 10h.01M14 10h.01M18 10h.01M6 14h.01M18 14h.01M9.5 14h5"),
  ],

  // A mouse with its left half filled in.
  leftClick: [
    { tag: "rect", attrs: { x: "5", y: "2", width: "14", height: "20", rx: "7" } },
    path("M5 10h14M12 2v8"),
    path("M12 2a7 7 0 0 0-7 7v1h7z", true),
  ],

  // The same mouse with its right half filled in.
  rightClick: [
    { tag: "rect", attrs: { x: "5", y: "2", width: "14", height: "20", rx: "7" } },
    path("M5 10h14M12 2v8"),
    path("M12 2a7 7 0 0 1 7 7v1h-7z", true),
  ],

  // A key cap with an arrow on it: the special-keys drawer.
  keys: [
    { tag: "rect", attrs: { x: "3", y: "4", width: "18", height: "16", rx: "3.5" } },
    path("M8.5 14.5 12 11l3.5 3.5"),
  ],

  // An eye: the screen previews.
  view: [
    path("M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"),
    { tag: "circle", attrs: { cx: "12", cy: "12", r: "3" } },
  ],

  // A monitor with an enlarging arrow: the display scale (100% / 200%).
  dpi: [
    { tag: "rect", attrs: { x: "3", y: "4", width: "18", height: "12", rx: "2" } },
    path("M8 20h8M12 16v4"),
    path("M10 13l4-4M11 9h3v3"),
  ],

  // Three sliders: settings.
  settings: [
    path("M4 6h8M18 6h2M4 12h2M12 12h8M4 18h10"),
    { tag: "circle", attrs: { cx: "15", cy: "6", r: "2.5" } },
    { tag: "circle", attrs: { cx: "9", cy: "12", r: "2.5" } },
    { tag: "circle", attrs: { cx: "17", cy: "18", r: "2.5" } },
  ],
};

const SVG_NS = "http://www.w3.org/2000/svg";

/** Builds an icon as an SVG element. It is decorative; the button that holds it carries the label. */
export function icon(name: IconName): SVGSVGElement {
  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  svg.classList.add("icon");

  for (const shape of ICONS[name]) {
    const el = document.createElementNS(SVG_NS, shape.tag);
    for (const [key, value] of Object.entries(shape.attrs)) el.setAttribute(key, value);
    if (shape.filled) {
      el.setAttribute("fill", "currentColor");
      el.setAttribute("stroke", "none");
    }
    svg.append(el);
  }

  return svg;
}
