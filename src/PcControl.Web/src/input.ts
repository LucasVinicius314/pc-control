// Sends input to the PC. Messages are specified in docs/protocol/websocket.md.

import type { Button, GestureOutput } from "./gestures";
import type { Link } from "./socket";

const MOVE_FRAME = 0x01;
const SCROLL_FRAME = 0x02;
const INT16_MAX = 32767;

export class InputChannel implements GestureOutput {
  private pendingX = 0;
  private pendingY = 0;
  private pendingWheelV = 0;
  private pendingWheelH = 0;
  private frame = 0;

  constructor(private readonly link: Link) {}

  /** Finger movement in phone pixels. Summed and sent once per animation frame. */
  move(dx: number, dy: number): void {
    this.pendingX += dx;
    this.pendingY += dy;
    this.schedule();
  }

  scroll(vertical: number, horizontal: number): void {
    this.pendingWheelV += vertical;
    this.pendingWheelH += horizontal;
    this.schedule();
  }

  click(button: Button): void {
    this.sendJson({ type: "button", button, action: "click" });
  }

  buttonDown(button: Button): void {
    this.sendJson({ type: "button", button, action: "down" });
  }

  buttonUp(button: Button): void {
    this.sendJson({ type: "button", button, action: "up" });
  }

  zoom(steps: number): void {
    this.sendJson({ type: "zoom", steps: Math.max(-10, Math.min(10, steps)) });
  }

  haptic(): void {
    try {
      navigator.vibrate?.(15);
    } catch {
      // Vibration is a nicety; some browsers refuse it.
    }
  }

  key(code: string, action: "down" | "up" | "press"): void {
    this.sendJson({ type: "key", code, action });
  }

  text(text: string): void {
    if (text.length > 0) this.sendJson({ type: "text", text });
  }

  setSensitivity(sensitivity: number): void {
    this.sendJson({ type: "settings", sensitivity });
  }

  releaseAll(): void {
    this.sendJson({ type: "releaseAll" });
  }

  /** Forget unsent movement, for example when the connection drops. */
  reset(): void {
    this.pendingX = this.pendingY = this.pendingWheelV = this.pendingWheelH = 0;
    window.cancelAnimationFrame(this.frame);
    this.frame = 0;
  }

  private schedule(): void {
    if (this.frame !== 0) return;
    this.frame = window.requestAnimationFrame(() => {
      this.frame = 0;
      this.flush();
    });
  }

  private flush(): void {
    // Whole numbers go out; the fractional part stays for the next frame so slow movement is kept.
    const dx = Math.trunc(this.pendingX);
    const dy = Math.trunc(this.pendingY);
    this.pendingX -= dx;
    this.pendingY -= dy;
    if (dx !== 0 || dy !== 0) this.sendFrame(MOVE_FRAME, dx, dy);

    const v = Math.trunc(this.pendingWheelV);
    const h = Math.trunc(this.pendingWheelH);
    this.pendingWheelV -= v;
    this.pendingWheelH -= h;
    if (v !== 0 || h !== 0) this.sendFrame(SCROLL_FRAME, v, h);
  }

  private sendFrame(kind: number, a: number, b: number): void {
    const buffer = new ArrayBuffer(5);
    const view = new DataView(buffer);
    view.setUint8(0, kind);
    view.setInt16(1, clamp(a), true);
    view.setInt16(3, clamp(b), true);
    this.link.send(buffer);
  }

  private sendJson(message: Record<string, unknown>): void {
    this.link.send(JSON.stringify(message));
  }
}

function clamp(value: number): number {
  return Math.max(-INT16_MAX, Math.min(INT16_MAX, value));
}
