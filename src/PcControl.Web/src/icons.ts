// The toolbar icons. They are small original line drawings on a 24 x 24 grid, drawn with the current
// text colour so they follow the button's state (normal, active, disabled). No icon library, no
// images to load: each one is a few shapes.

export type IconName = "keyboard" | "leftClick" | "rightClick" | "keys" | "apps" | "search" | "view" | "dpi" | "media" | "power" | "settings" | "lock" | "sleep" | "signOut" | "restart" | "closeX" | "force" | "link";

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

  // Four tiles: the app launcher.
  apps: [
    { tag: "rect", attrs: { x: "3", y: "3", width: "7", height: "7", rx: "1.8" } },
    { tag: "rect", attrs: { x: "14", y: "3", width: "7", height: "7", rx: "1.8" } },
    { tag: "rect", attrs: { x: "3", y: "14", width: "7", height: "7", rx: "1.8" } },
    { tag: "rect", attrs: { x: "14", y: "14", width: "7", height: "7", rx: "1.8" } },
  ],

  // A magnifying glass: search, and the PowerToys Run button.
  search: [{ tag: "circle", attrs: { cx: "11", cy: "11", r: "6.5" } }, path("M20 20l-4.2-4.2")],

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

  // A play triangle in a circle: media controls.
  media: [{ tag: "circle", attrs: { cx: "12", cy: "12", r: "9.5" } }, path("M10 8.2v7.6l6-3.8z", true)],

  // The power symbol: a ring with a gap and a bar through it.
  power: [path("M7.5 6.6a7.5 7.5 0 1 0 9 0"), path("M12 3v8")],

  // A padlock.
  lock: [
    { tag: "rect", attrs: { x: "5", y: "11", width: "14", height: "9", rx: "2.5" } },
    path("M8 11V8a4 4 0 0 1 8 0v3"),
  ],

  // A crescent moon: sleep.
  sleep: [path("M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z")],

  // An arrow leaving a doorway: sign out.
  signOut: [path("M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4"), path("M15.5 8 20 12l-4.5 4M20 12H10")],

  // A circular arrow: restart.
  restart: [path("M4.5 12a7.5 7.5 0 1 0 2.4-5.5"), path("M4 4v4.5h4.5")],

  // A cross: close.
  closeX: [path("M6 6l12 12M18 6 6 18")],

  // A lightning bolt: end it right now.
  force: [path("M13 3 5 14h6l-1 7 8-11h-6z")],

  // Two chain links: open a link.
  link: [
    path("M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"),
    path("M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"),
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
