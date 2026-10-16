// What the system panel (open a link, power, close programs) says and when a tap counts as confirmed.
// No DOM here.

import { ApiError } from "./api";

export type PowerName = "lock" | "sleep" | "restart" | "shutdown" | "signOut";

/** Actions that end the session need a second tap on the phone (and `confirm` on the wire). */
export function needsConfirmation(action: PowerName): boolean {
  return action === "restart" || action === "shutdown" || action === "signOut";
}

/**
 * Two taps on the same button within a short time. The first arms it (the button then asks "tap again
 * to confirm"); the second, if it comes soon enough, is the confirmation. A tap elsewhere disarms it.
 */
export class ConfirmGate {
  private armed: string | null = null;
  private armedAt = 0;

  constructor(
    private readonly windowMs = 4000,
    private readonly now: () => number = () => performance.now(),
  ) {}

  /** True when this tap confirms an earlier one on the same key. Otherwise arms the key and returns false. */
  tap(key: string): boolean {
    const t = this.now();
    if (this.armed === key && t - this.armedAt <= this.windowMs) {
      this.armed = null;
      return true;
    }
    this.armed = key;
    this.armedAt = t;
    return false;
  }

  isArmed(key: string): boolean {
    return this.armed === key && this.now() - this.armedAt <= this.windowMs;
  }

  reset(): void {
    this.armed = null;
  }
}

const NETWORK = "Could not reach the PC. Check that you are on the same Wi-Fi.";

function common(error: unknown): string | null {
  if (error instanceof ApiError) {
    if (error.code === "paused") return "Remote control is paused on the PC.";
    if (error.code === "feature_disabled") return "That is turned off on the PC.";
    if (error.status === 401) return "This phone is no longer paired.";
    return null;
  }
  return NETWORK;
}

export function urlMessage(error: unknown): string {
  const shared = common(error);
  if (shared) return shared;
  if (error instanceof ApiError) {
    if (error.code === "invalid_url") return "Only web addresses (starting with http or https) can be opened.";
    if (error.code === "open_failed") return error.detail ?? "Windows could not open that address.";
  }
  return NETWORK;
}

export function powerMessage(error: unknown): string {
  const shared = common(error);
  if (shared) return shared;
  if (error instanceof ApiError) {
    if (error.code === "power_failed") return error.detail ?? "Windows did not carry that out.";
    if (error.code === "nothing_pending") return "Nothing is waiting to be cancelled.";
  }
  return NETWORK;
}

export function processMessage(error: unknown): string {
  const shared = common(error);
  if (shared) return shared;
  if (error instanceof ApiError) {
    if (error.code === "unknown_process" || error.code === "process_gone") return "That program is already closed. The list was refreshed.";
    if (error.code === "focus_failed") return error.detail ?? "Windows would not bring that program to the front.";
    if (error.code === "close_failed") return error.detail ?? "The program did not close. Try Force close.";
  }
  return NETWORK;
}

/** The sentence under the power buttons while a restart or shutdown is counting down. */
export function countdownText(action: string, secondsLeft: number): string {
  const what = action === "restart" ? "Restarting" : action === "shutdown" ? "Shutting down" : "Working";
  return `${what} in ${Math.max(0, Math.ceil(secondsLeft))} s`;
}

/** What to say after a power action went through. */
export function powerDone(action: PowerName, delaySeconds: number): string {
  switch (action) {
    case "lock":
      return "PC locked.";
    case "sleep":
      return "PC going to sleep.";
    case "signOut":
      return "Signing out.";
    default:
      return `${countdownText(action, delaySeconds)}. You can cancel it.`;
  }
}
