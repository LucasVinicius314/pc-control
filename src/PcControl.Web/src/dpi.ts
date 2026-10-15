// The display-scale button: what its badge says and what to tell the user. No DOM here.

import { ApiError, type DpiChange, type DpiStatus } from "./api";

/** The text under the button's icon: the current scale, or a dash when it is not known. */
export function badgeText(status: Pick<DpiStatus, "available" | "percent"> | null): string {
  return status?.available && status.percent ? `${status.percent}%` : "–";
}

/** What to tell the user after a successful change. Many apps keep their old size until restarted. */
export function changedMessage(change: Pick<DpiChange, "percent" | "note">): string {
  return `Display scale is now ${change.percent}%. ${change.note}`;
}

/** A plain sentence for a failed change. The PC's own explanation wins when it sent one. */
export function failureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case "busy":
        return "A scale change is already in progress.";
      case "dpi_unavailable":
        return error.detail ?? "The display scale toggle is turned off on the PC.";
      case "scale_not_supported":
      case "dpi_failed":
        return error.detail ?? "Windows did not change the scale.";
      default:
        if (error.status === 401) return "This phone is no longer paired.";
    }
  }
  return "Could not reach the PC. Check that you are on the same Wi-Fi.";
}
