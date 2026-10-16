// The media remote: what to press while watching something on the PC. Shared by the phone page and the
// controller UI. A big play/pause with previous, next and 10-second jumps around it, then smaller
// groups for 5-second jumps, the PC's volume, and the player.

import { icon, type IconName } from "./icons";

/** What the remote needs from the connection: press one key. */
export interface MediaKeys {
  key: (code: string, action: "press") => void;
}

export interface RemoteButton {
  icon: IconName;
  /** The key to press, named like the browser's KeyboardEvent.code. */
  code: string;
  /** What it does, for tooltips, screen readers and the controller UI's footer. */
  hint: string;
  /** A small number inside the button, for the jump buttons ("10" for ten seconds). */
  seconds?: string;
  /** The main button, drawn larger and in the accent colour. */
  primary?: boolean;
  /** Holding the button keeps repeating the key, like holding it on a keyboard. */
  repeat?: boolean;
}

export const REMOTE_MAIN: RemoteButton[] = [
  { icon: "skipPrev", code: "MediaTrackPrevious", hint: "Previous track or episode" },
  { icon: "back", code: "KeyJ", hint: "Back 10 seconds", seconds: "10", repeat: true },
  { icon: "playPause", code: "MediaPlayPause", hint: "Play or pause", primary: true },
  { icon: "forward", code: "KeyL", hint: "Forward 10 seconds", seconds: "10", repeat: true },
  { icon: "skipNext", code: "MediaTrackNext", hint: "Next track or episode" },
];

export const REMOTE_CLUSTERS: { caption: string; buttons: RemoteButton[] }[] = [
  {
    caption: "Jump 5 s",
    buttons: [
      { icon: "back", code: "ArrowLeft", hint: "Back 5 seconds", seconds: "5", repeat: true },
      { icon: "forward", code: "ArrowRight", hint: "Forward 5 seconds", seconds: "5", repeat: true },
    ],
  },
  {
    caption: "PC volume",
    buttons: [
      { icon: "volumeDown", code: "AudioVolumeDown", hint: "PC volume down", repeat: true },
      { icon: "mute", code: "AudioVolumeMute", hint: "Mute or unmute the PC" },
      { icon: "volumeUp", code: "AudioVolumeUp", hint: "PC volume up", repeat: true },
    ],
  },
  {
    caption: "In the player",
    buttons: [
      { icon: "fullscreen", code: "KeyF", hint: "Fullscreen on or off" },
      { icon: "chevronDown", code: "ArrowDown", hint: "Player volume down", repeat: true },
      { icon: "chevronUp", code: "ArrowUp", hint: "Player volume up", repeat: true },
      { icon: "closeX", code: "Escape", hint: "Leave fullscreen" },
    ],
  },
];

/** The phone's media panel: the same remote, made for thumbs. */
export function createMediaPanel(keys: MediaKeys): HTMLElement {
  const panel = document.createElement("section");
  panel.className = "mremote card";
  panel.hidden = true;

  const round = (b: RemoteButton, size: "lg" | "md" | "sm"): HTMLButtonElement => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `mround ${size}${b.primary ? " primary" : ""}`;
    button.title = b.hint;
    button.setAttribute("aria-label", b.hint);
    button.append(icon(b.icon));
    if (b.seconds) {
      const seconds = document.createElement("span");
      seconds.className = "mround-seconds";
      seconds.textContent = b.seconds;
      button.append(seconds);
    }
    // Keep focus where it is, so tapping does not close the on-screen keyboard.
    button.addEventListener("pointerdown", (e) => e.preventDefault());

    if (!b.repeat) {
      button.addEventListener("click", () => keys.key(b.code, "press"));
      return button;
    }

    // The first touch acts at once; holding keeps going after a short pause.
    let delay = 0;
    let timer = 0;
    const stop = () => {
      window.clearTimeout(delay);
      window.clearInterval(timer);
    };
    button.addEventListener("pointerdown", () => {
      keys.key(b.code, "press");
      stop();
      delay = window.setTimeout(() => (timer = window.setInterval(() => keys.key(b.code, "press"), 120)), 400);
    });
    for (const ev of ["pointerup", "pointercancel", "pointerleave"]) button.addEventListener(ev, stop);
    return button;
  };

  const main = document.createElement("div");
  main.className = "mremote-main";
  main.append(...REMOTE_MAIN.map((b) => round(b, b.primary ? "lg" : "md")));

  const extras = document.createElement("div");
  extras.className = "mremote-extras";
  for (const cluster of REMOTE_CLUSTERS) {
    const group = document.createElement("div");
    group.className = "mremote-cluster";
    const buttons = document.createElement("div");
    buttons.className = "mremote-buttons";
    buttons.append(...cluster.buttons.map((b) => round(b, "sm")));
    const caption = document.createElement("div");
    caption.className = "mremote-caption";
    caption.textContent = cluster.caption;
    group.append(buttons, caption);
    extras.append(group);
  }

  panel.append(main, extras);
  return panel;
}
