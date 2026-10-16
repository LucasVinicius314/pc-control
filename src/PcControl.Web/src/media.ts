// The media panel: what to press while watching something on the PC. Grouped by what you are doing:
// play / skip, jump around in the video, the PC's volume, and the player itself.

import type { IconName } from "./icons";
import { heading, tile } from "./system";

/** What the panel needs from the connection: press one key. */
export interface MediaKeys {
  key: (code: string, action: "press") => void;
}

interface Control {
  icon: IconName;
  label: string;
  code: string;
  hint: string;
  /** Holding the button repeats the key, like holding it on a keyboard. */
  repeat?: boolean;
  /** The main button of its group, drawn larger and in the accent colour. */
  primary?: boolean;
}

const TRANSPORT: Control[] = [
  { icon: "skipPrev", label: "Previous", code: "MediaTrackPrevious", hint: "Previous track or episode" },
  { icon: "playPause", label: "Play / pause", code: "MediaPlayPause", hint: "Play or pause whatever is playing", primary: true },
  { icon: "skipNext", label: "Next", code: "MediaTrackNext", hint: "Next track or episode" },
];

const SEEK: Control[] = [
  { icon: "back", label: "−10 s", code: "KeyJ", hint: "Back 10 seconds (J)", repeat: true },
  { icon: "back", label: "−5 s", code: "ArrowLeft", hint: "Back 5 seconds (left arrow)", repeat: true },
  { icon: "forward", label: "+5 s", code: "ArrowRight", hint: "Forward 5 seconds (right arrow)", repeat: true },
  { icon: "forward", label: "+10 s", code: "KeyL", hint: "Forward 10 seconds (L)", repeat: true },
];

const PC_VOLUME: Control[] = [
  { icon: "volumeDown", label: "Quieter", code: "AudioVolumeDown", hint: "PC volume down", repeat: true },
  { icon: "mute", label: "Mute", code: "AudioVolumeMute", hint: "Mute or unmute the PC" },
  { icon: "volumeUp", label: "Louder", code: "AudioVolumeUp", hint: "PC volume up", repeat: true },
];

const PLAYER: Control[] = [
  { icon: "fullscreen", label: "Fullscreen", code: "KeyF", hint: "Fullscreen on or off (F)" },
  { icon: "chevronDown", label: "Quieter", code: "ArrowDown", hint: "Player volume down (down arrow)", repeat: true },
  { icon: "chevronUp", label: "Louder", code: "ArrowUp", hint: "Player volume up (up arrow)", repeat: true },
  { icon: "closeX", label: "Esc", code: "Escape", hint: "Leave fullscreen (Esc)" },
];

export function createMediaPanel(keys: MediaKeys): HTMLElement {
  const panel = document.createElement("section");
  panel.className = "media-panel card";
  panel.hidden = true;

  const control = (c: Control): HTMLButtonElement => {
    const { button } = tile(c.primary ? "tile primary" : "tile", c.icon, c.label, c.hint);
    // Keep focus where it is, so tapping does not close the on-screen keyboard.
    button.addEventListener("pointerdown", (e) => e.preventDefault());

    if (!c.repeat) {
      button.addEventListener("click", () => keys.key(c.code, "press"));
      return button;
    }

    // Pointer events instead of click: the first press acts at once, holding keeps going.
    let delay = 0;
    let timer = 0;
    const stop = () => {
      window.clearTimeout(delay);
      window.clearInterval(timer);
    };
    button.addEventListener("pointerdown", () => {
      keys.key(c.code, "press");
      stop();
      delay = window.setTimeout(() => (timer = window.setInterval(() => keys.key(c.code, "press"), 120)), 400);
    });
    for (const ev of ["pointerup", "pointercancel", "pointerleave"]) button.addEventListener(ev, stop);
    return button;
  };

  const group = (title: string, columns: number, controls: Control[], className = ""): HTMLElement[] => {
    const grid = document.createElement("div");
    grid.className = `media-grid ${className}`.trim();
    grid.style.setProperty("grid-template-columns", `repeat(${columns}, 1fr)`);
    grid.append(...controls.map(control));
    return [heading(title), grid];
  };

  panel.append(
    ...group("Playback", 3, TRANSPORT, "transport"),
    ...group("Jump", 4, SEEK),
    ...group("PC volume", 3, PC_VOLUME),
    ...group("In the player", 4, PLAYER),
  );
  return panel;
}
