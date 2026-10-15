// The mini preview (area around the cursor) and the display preview (whole monitor): receives
// pictures and cursor positions from the PC, draws them, and tells the PC what to stream.
//
// The display preview's cursor ring is NOT part of the picture. Pictures arrive a few times a
// second and are always a little old; the cursor arrives on its own, up to 60 times a second, and
// the ring is an overlay moved with a transform, so it keeps up with the finger.

import {
  buildViewMessages,
  type Channel,
  containedRect,
  CURSOR_KIND,
  type Frame,
  parseCursor,
  parseFrame,
  sanitizePrefs,
  STATUS_NOTES,
  type ViewPrefs,
} from "./viewlogic";

const PREFS_KEY = "pc-control.view";

export function loadPrefs(): ViewPrefs {
  try {
    return sanitizePrefs(JSON.parse(localStorage.getItem(PREFS_KEY) ?? "null"));
  } catch {
    return sanitizePrefs(null);
  }
}

function savePrefs(prefs: ViewPrefs): void {
  try {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs));
  } catch {
    // The settings just will not be remembered.
  }
}

export interface ViewElements {
  pad: HTMLElement;
  display: HTMLCanvasElement;
  mini: HTMLCanvasElement;
  /** The ring that marks the cursor on the display preview. */
  ring: HTMLElement;
  note: HTMLElement;
}

export class ViewClient {
  private online = false;
  private readonly states: Record<Channel, string> = { magnifier: "running", display: "running" };
  private readonly waiting: Record<Channel, Frame | null> = { magnifier: null, display: null };
  private readonly drawing: Record<Channel, boolean> = { magnifier: false, display: false };
  private resizeTimer = 0;

  // Where the cursor is, and which monitor the display picture shows.
  private cursor: { monitor: number; x: number; y: number } | null = null;
  private shownMonitor: number | null = null;
  private ringFrame = 0;

  constructor(
    private readonly send: (message: Record<string, unknown>) => void,
    private readonly els: ViewElements,
    private current: ViewPrefs,
  ) {
    // A hidden page must not keep the PC capturing its screen; resume when it is shown again.
    document.addEventListener("visibilitychange", () => this.sync());

    // A turned phone changes the display preview's size.
    new ResizeObserver(() => {
      window.clearTimeout(this.resizeTimer);
      this.resizeTimer = window.setTimeout(() => this.sync(), 300);
      this.placeRing();
    }).observe(els.pad);

    this.applyLayout();
  }

  get prefs(): ViewPrefs {
    return this.current;
  }

  setPrefs(next: ViewPrefs): void {
    this.current = next;
    savePrefs(next);
    this.applyLayout();
    this.sync();
  }

  setOnline(online: boolean): void {
    this.online = online;
    if (!online) {
      this.states.magnifier = this.states.display = "running";
      this.cursor = null;
      this.shownMonitor = null;
      this.updateNote();
      this.placeRing();
    }
    this.sync();
  }

  /** Sends the PC what to stream. Safe to call any time; it also switches off what should be off. */
  sync(): void {
    if (!this.online) return;
    for (const message of buildViewMessages(this.current, {
      online: this.online,
      visible: document.visibilityState === "visible",
      padWidth: this.els.pad.clientWidth,
      devicePixelRatio: window.devicePixelRatio || 1,
    })) {
      this.send(message);
    }
  }

  handleBinary(data: ArrayBuffer): void {
    // Cursor messages are the hot path: 6 bytes, many per second. Handle them first and cheaply.
    if (data.byteLength === 6 && new Uint8Array(data, 0, 1)[0] === CURSOR_KIND) {
      this.handleCursor(data);
      return;
    }

    const frame = parseFrame(data);
    if (!frame) return;

    // A picture can arrive just after the preview was switched off.
    if ((frame.channel === "display" && !this.current.display) || (frame.channel === "magnifier" && !this.current.mini)) return;

    this.states[frame.channel] = "running";
    this.updateNote();

    // Latest picture wins: if the previous one is still being decoded, this replaces any waiting one.
    this.waiting[frame.channel] = frame;
    if (!this.drawing[frame.channel]) void this.drawLoop(frame.channel);
  }

  handleStatus(stream: string, state: string): void {
    if (stream !== "magnifier" && stream !== "display") return;
    this.states[stream] = state;
    this.updateNote();
  }

  private handleCursor(data: ArrayBuffer): void {
    const update = parseCursor(data);
    if (!update || !this.current.display) return;
    this.cursor = update;
    this.placeRing();
  }

  /** Moves the ring at most once per animation frame, however many cursor messages arrived. */
  private placeRing(): void {
    if (this.ringFrame !== 0) return;
    this.ringFrame = window.requestAnimationFrame(() => {
      this.ringFrame = 0;
      this.positionRing();
    });
  }

  private positionRing(): void {
    const { display, ring, pad } = this.els;
    const c = this.cursor;

    // Hide the ring unless the cursor is on the monitor the picture shows.
    if (!this.current.display || !c || c.monitor < 0 || this.shownMonitor !== c.monitor || display.width === 0) {
      ring.hidden = true;
      return;
    }

    const box = containedRect(pad.clientWidth, pad.clientHeight, display.width, display.height);
    if (box.width === 0) {
      ring.hidden = true;
      return;
    }

    ring.hidden = false;
    ring.style.transform = `translate3d(${(box.left + c.x * box.width).toFixed(1)}px, ${(box.top + c.y * box.height).toFixed(1)}px, 0)`;
  }

  private async drawLoop(channel: Channel): Promise<void> {
    this.drawing[channel] = true;
    try {
      for (let frame = this.waiting[channel]; frame; frame = this.waiting[channel]) {
        this.waiting[channel] = null;
        await this.draw(frame);
      }
    } finally {
      this.drawing[channel] = false;
    }
  }

  private async draw(frame: Frame): Promise<void> {
    const canvas = frame.channel === "display" ? this.els.display : this.els.mini;
    let bitmap: ImageBitmap;
    try {
      bitmap = await createImageBitmap(new Blob([frame.jpeg as BlobPart], { type: "image/jpeg" }));
    } catch {
      return; // a damaged picture; the next one will do
    }

    if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
    }

    const ctx = canvas.getContext("2d");
    if (ctx) {
      ctx.drawImage(bitmap, 0, 0);
      // The mini preview's marker is part of its picture: the region is centred on the cursor, so
      // the marker barely moves and there is nothing to keep up with.
      if (frame.channel === "magnifier" && frame.cursorX >= 0 && frame.cursorY >= 0) this.drawMarker(ctx, canvas, frame);
    }
    bitmap.close();

    if (frame.channel === "display") {
      this.shownMonitor = frame.monitor;
      this.placeRing();
    }
  }

  /** The screen capture has no mouse cursor, so draw a ring where it is. */
  private drawMarker(ctx: CanvasRenderingContext2D, canvas: HTMLCanvasElement, frame: Frame): void {
    // Size the marker in on-screen pixels, whatever the picture is scaled by.
    const shown = canvas.clientWidth || canvas.width;
    const scale = shown / canvas.width;
    const radius = 9 / scale;
    const line = 2 / scale;

    ctx.lineWidth = line * 2;
    ctx.strokeStyle = "rgba(0,0,0,0.85)";
    ctx.beginPath();
    ctx.arc(frame.cursorX, frame.cursorY, radius, 0, Math.PI * 2);
    ctx.stroke();

    ctx.lineWidth = line;
    ctx.strokeStyle = "#ffd23f";
    ctx.beginPath();
    ctx.arc(frame.cursorX, frame.cursorY, radius, 0, Math.PI * 2);
    ctx.stroke();
  }

  private applyLayout(): void {
    const { display, mini } = this.els;
    display.hidden = !this.current.display;
    mini.hidden = !this.current.mini;
    mini.dataset["corner"] = this.current.corner;
    // Shrinking looks jagged with hard pixels; enlarging looks best with them.
    mini.dataset["smooth"] = this.current.miniZoom < 1 ? "1" : "0";
    this.els.pad.classList.toggle("has-display", this.current.display);

    if (!this.current.display) {
      display.getContext("2d")?.clearRect(0, 0, display.width, display.height);
      this.cursor = null;
      this.shownMonitor = null;
    }
    if (!this.current.mini) mini.getContext("2d")?.clearRect(0, 0, mini.width, mini.height);
    this.updateNote();
    this.placeRing();
  }

  private updateNote(): void {
    const wanted: Channel[] = [];
    if (this.current.display) wanted.push("display");
    if (this.current.mini) wanted.push("magnifier");

    const problem = wanted.map((c) => this.states[c]).find((s) => s !== "running");
    const text = problem ? STATUS_NOTES[problem] ?? "" : "";
    this.els.note.textContent = text;
    this.els.note.hidden = text === "";
  }
}
