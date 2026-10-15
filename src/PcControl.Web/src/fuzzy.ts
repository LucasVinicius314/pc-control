// Search for the app list. Typing "vs code" finds "Visual Studio Code"; "cafe" finds "Café". It runs
// on the phone over the list it already has, so results change as fast as you type.

/** Lower case with accents removed, so "Édge" and "edge" compare equal. */
export function normalize(text: string): string {
  return text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();
}

const EXACT = 1000;
const PREFIX = 900;
const WORD_START = 700;
const INITIALS = 600;
const SUBSTRING = 500;
const SUBSEQUENCE = 300;

/** Letters of a subsequence match may be spread over at most this many times their own count. */
const MAX_SPREAD = 3;

function isWordStart(name: string, index: number): boolean {
  return index === 0 || /[\s\-_.()[\]/\\]/.test(name.charAt(index - 1));
}

/** How well one word of the query matches the name, or null when it does not. Higher is better. */
function scoreToken(token: string, name: string): number | null {
  if (name === token) return EXACT;
  if (name.startsWith(token)) return PREFIX - Math.min(name.length - token.length, 99);

  // The earliest place the token starts a word, or failing that, appears anywhere.
  let wordStart = -1;
  let anywhere = -1;
  for (let i = name.indexOf(token); i >= 0; i = name.indexOf(token, i + 1)) {
    if (anywhere < 0) anywhere = i;
    if (isWordStart(name, i)) {
      wordStart = i;
      break;
    }
  }
  if (wordStart >= 0) return WORD_START - Math.min(wordStart, 99);
  if (anywhere >= 0) return SUBSTRING - Math.min(anywhere, 99);

  return scoreInitials(token, name) ?? scoreSubsequence(token, name);
}

/** Each letter starts a later word: "vsc" is Visual Studio Code, "gc" is Google Chrome. */
function scoreInitials(token: string, name: string): number | null {
  if (token.length < 2) return null;

  const words = name.split(/[\s\-_.()[\]/\\]+/).filter(Boolean);
  // The letters must begin consecutive words, starting at any word: "sc" matches "Visual Studio Code".
  for (let start = 0; start + token.length <= words.length; start++) {
    if ([...token].every((ch, k) => words[start + k]!.startsWith(ch))) {
      return INITIALS - Math.min(start, 50);
    }
  }

  return null;
}

/** The letters appear in order ("vscode" in "visual studio code"), if they are not too spread out. */
function scoreSubsequence(token: string, name: string): number | null {
  if (token.length < 2) return null;

  let from = 0;
  let first = -1;
  let last = -1;
  let wordStarts = 0;
  for (const ch of token) {
    const at = name.indexOf(ch, from);
    if (at < 0) return null;
    if (first < 0) first = at;
    if (isWordStart(name, at)) wordStarts++;
    last = at;
    from = at + 1;
  }

  const spread = last - first + 1;
  if (spread > token.length * MAX_SPREAD) return null;
  return SUBSEQUENCE + wordStarts * 10 - (spread - token.length);
}

/**
 * Scores a name against a query, or returns null when it does not match. Every word of the query
 * has to match. An empty query matches everything with the same score, so nothing is reordered.
 */
export function scoreMatch(query: string, name: string): number | null {
  const tokens = normalize(query).split(/\s+/).filter(Boolean);
  if (tokens.length === 0) return 0;

  const target = normalize(name);
  let total = 0;
  for (const token of tokens) {
    const score = scoreToken(token, target);
    if (score === null) return null;
    total += score;
  }

  // Between two equally good matches, the shorter name is the closer one.
  return total - Math.min(target.length, 99) / 100;
}

export interface Ranked<T> {
  item: T;
  score: number;
}

/** The items that match, best first. With an empty query everything is returned in its original order. */
export function rank<T>(query: string, items: readonly T[], nameOf: (item: T) => string): Ranked<T>[] {
  const matches: Ranked<T>[] = [];
  for (const item of items) {
    const score = scoreMatch(query, nameOf(item));
    if (score !== null) matches.push({ item, score });
  }

  if (query.trim() === "") return matches;
  return matches.sort((a, b) => b.score - a.score || nameOf(a.item).localeCompare(nameOf(b.item)));
}
