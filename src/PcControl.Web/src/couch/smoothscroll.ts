// Scrolls the focused item into view with our own easing. The browser's own "smooth" scrolling is switched off
// by Windows' "animation effects" setting and cut short by every new key press, which made a long jump
// between two rows snap. This one always glides, and a new press simply picks up from where it is.

/** How long a glide of this many pixels takes: short hops are quick, long ones a little slower, never sluggish. */
export function glideMs(distance: number): number {
  return Math.min(360, 160 + Math.abs(distance) * 0.35);
}

/** 0 to 1 in, 0 to 1 out: starts fast and settles gently. */
export function easeOut(t: number): number {
  const x = Math.min(1, Math.max(0, t));
  return 1 - Math.pow(1 - x, 3);
}

const running = new WeakMap<HTMLElement, number>();

function glide(container: HTMLElement, to: number): void {
  const previous = running.get(container);
  if (previous !== undefined) cancelAnimationFrame(previous);

  const from = container.scrollTop;
  const distance = to - from;
  if (Math.abs(distance) < 1) {
    container.scrollTop = to;
    return;
  }

  const duration = glideMs(distance);
  const start = performance.now();
  const step = (now: number): void => {
    const t = (now - start) / duration;
    container.scrollTop = from + distance * easeOut(t);
    if (t < 1) running.set(container, requestAnimationFrame(step));
    else running.delete(container);
  };
  running.set(container, requestAnimationFrame(step));
}

/** The scrolling containers an item sits in, nearest first, up to (not including) `stop`. */
export function scrollersAbove(item: HTMLElement, stop: HTMLElement): HTMLElement[] {
  const found: HTMLElement[] = [];
  for (let e = item.parentElement; e && e !== stop; e = e.parentElement) {
    const overflow = getComputedStyle(e).overflowY;
    if ((overflow === "auto" || overflow === "scroll") && e.scrollHeight > e.clientHeight) found.push(e);
  }
  return found;
}

/** Brings an item into view the way `scrollIntoView({ block: "nearest" })` would, but gliding there. */
export function glideIntoView(item: HTMLElement, stop: HTMLElement): void {
  const scrollers = scrollersAbove(item, stop);
  // Wherever each one is heading right now (a glide may be under way), so a quick second press works from there.
  const before = scrollers.map((s) => s.scrollTop);
  item.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "instant" });
  const targets = scrollers.map((s) => s.scrollTop);
  scrollers.forEach((s, i) => {
    s.scrollTop = before[i] ?? 0;
    glide(s, targets[i] ?? 0);
  });
}

/** Glides a scroller to a position (for example the very top). */
export function glideTo(container: HTMLElement, top: number): void {
  glide(container, top);
}
