// Phone keyboards compose words (suggestions, autocorrect) and keep changing the unfinished text.
// To type that live on the PC, we send only what changed: backspaces for the part that was
// replaced, then the new tail.

export interface TextEdit {
  backspaces: number;
  insert: string;
}

/** What to send so the PC's text goes from `previous` to `next`, comparing whole characters. */
export function diffComposition(previous: string, next: string): TextEdit {
  const a = Array.from(previous);
  const b = Array.from(next);

  let common = 0;
  while (common < a.length && common < b.length && a[common] === b[common]) common++;

  return { backspaces: a.length - common, insert: b.slice(common).join("") };
}
