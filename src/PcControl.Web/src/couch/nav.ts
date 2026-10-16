// Moving the focus around a screen with a D-pad or stick: from the focused item, go to the nearest
// item in that direction. Pure geometry, so it is tested without a browser.

export type Direction = "up" | "down" | "left" | "right";

export interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface Candidate {
  id: number;
  box: Box;
}

const center = (b: Box) => ({ x: b.x + b.width / 2, y: b.y + b.height / 2 });

/** How much of `a` and `b` overlap along one axis, as a fraction of the smaller one (0 to 1). */
function overlap(aStart: number, aSize: number, bStart: number, bSize: number): number {
  const shared = Math.min(aStart + aSize, bStart + bSize) - Math.max(aStart, bStart);
  return shared <= 0 ? 0 : shared / Math.min(aSize, bSize);
}

/**
 * The best item to move to, or null when there is nothing that way. An item has to lie in the
 * direction of travel; among those, one that lines up with the current item beats one that is merely
 * close, so moving right along a row stays on the row.
 */
export function pickNext(from: Box, candidates: readonly Candidate[], direction: Direction): number | null {
  const horizontal = direction === "left" || direction === "right";
  const sign = direction === "right" || direction === "down" ? 1 : -1;
  const c = center(from);

  let best: { id: number; score: number } | null = null;
  for (const candidate of candidates) {
    const o = center(candidate.box);
    // Distance along the direction of travel, and sideways from the current item's centre line.
    const along = ((horizontal ? o.x - c.x : o.y - c.y)) * sign;
    const across = Math.abs(horizontal ? o.y - c.y : o.x - c.x);
    if (along <= 1) continue; // not that way (or the item itself)

    const lined = horizontal
      ? overlap(from.y, from.height, candidate.box.y, candidate.box.height)
      : overlap(from.x, from.width, candidate.box.x, candidate.box.width);

    // Lined-up items win by a wide margin; otherwise sideways drift counts double.
    const score = lined > 0.3 ? along + across * 0.25 : along + across * 2 + 100000;
    if (best === null || score < best.score) best = { id: candidate.id, score };
  }
  return best?.id ?? null;
}
