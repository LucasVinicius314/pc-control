// What the app list shows and what to tell the user when something goes wrong. No DOM here.

import { ApiError, type AppItem } from "./api";
import { rank } from "./fuzzy";

export interface Section {
  title: string;
  apps: AppItem[];
}

/** How many recently used apps are shown above the full list. */
export const RECENT_SHOWN = 8;

/**
 * The list as sections. Without a search: favorites, then recently used, then every app. With a
 * search: one list of the matches, best first (or none).
 */
export function buildSections(apps: readonly AppItem[], recent: readonly string[], query: string): Section[] {
  if (query.trim() !== "") {
    const matches = rank(query, apps, (a) => a.name).map((r) => r.item);
    return matches.length > 0 ? [{ title: "Results", apps: matches }] : [];
  }

  const byId = new Map(apps.map((a) => [a.id, a]));
  const favorites = apps.filter((a) => a.favorite);
  const recents = recent
    .map((id) => byId.get(id))
    .filter((a): a is AppItem => a !== undefined)
    .slice(0, RECENT_SHOWN);

  const sections: Section[] = [];
  if (favorites.length > 0) sections.push({ title: "Favorites", apps: favorites });
  if (recents.length > 0) sections.push({ title: "Recent", apps: recents });
  sections.push({ title: "All apps", apps: [...apps] });
  return sections;
}

/** A sentence for a failed launch or favorite change. */
export function launchFailureMessage(error: unknown, appName?: string): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case "paused":
        return "Remote control is paused on the PC.";
      case "unknown_app":
        return "That app is no longer in the Start menu. The list will refresh.";
      case "launch_failed":
        return error.detail ?? `Windows could not start ${appName ?? "that app"}.`;
      default:
        if (error.status === 401) return "This phone is no longer paired.";
    }
  }
  return "Could not reach the PC. Check that you are on the same Wi-Fi.";
}

/** The message after pressing the PowerToys Run button. */
export function powerToysMessage(shortcut: string, detected: boolean): string {
  return detected
    ? `Opened PowerToys Run (${shortcut}). Type on the keyboard.`
    : `Pressed ${shortcut}. If PowerToys Run did not open, check its shortcut in PowerToys settings.`;
}
