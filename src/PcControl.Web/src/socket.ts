// WebSocket link with automatic reconnect. Message layout: docs/protocol/websocket.md.

export type LinkState = "connecting" | "connected" | "paused" | "reconnecting" | "unauthorized";

export interface LinkEvents {
  onState: (state: LinkState) => void;
  /** The server refused or could not apply a message (for example an unknown key). */
  onServerError?: (code: string) => void;
  /** A picture from the PC (mini preview or display preview). */
  onBinary?: (data: ArrayBuffer) => void;
  /** A preview stream changed state, for example because the PC was locked. */
  onViewStatus?: (stream: string, state: string) => void;
}

const PING_INTERVAL_MS = 10_000;
const PONG_TIMEOUT_MS = 6_000;
const BACKOFF_MS = [500, 1000, 2000, 4000, 8000];

export class Link {
  private socket: WebSocket | null = null;
  private attempt = 0;
  private closedByUser = false;
  private pingTimer = 0;
  private pongTimer = 0;
  private reconnectTimer = 0;

  constructor(private readonly events: LinkEvents) {
    // A phone that slept has a dead socket. Check as soon as the page is visible again.
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "visible" && this.socket?.readyState !== WebSocket.OPEN) {
        this.connectNow();
      }
    });
  }

  connect(): void {
    this.closedByUser = false;
    this.connectNow();
  }

  /** Sends a frame if the socket is open. Input is never queued: stale input is worse than none. */
  send(data: string | ArrayBuffer): boolean {
    if (this.socket?.readyState !== WebSocket.OPEN) return false;
    this.socket.send(data);
    return true;
  }

  get isOpen(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  close(): void {
    this.closedByUser = true;
    this.teardown();
    this.socket?.close(1000);
    this.socket = null;
  }

  private connectNow(): void {
    window.clearTimeout(this.reconnectTimer);
    if (this.socket && this.socket.readyState <= WebSocket.OPEN) return;

    this.events.onState(this.attempt === 0 ? "connecting" : "reconnecting");

    const scheme = location.protocol === "https:" ? "wss:" : "ws:";
    const socket = new WebSocket(`${scheme}//${location.host}/ws`);
    socket.binaryType = "arraybuffer";
    this.socket = socket;

    socket.onmessage = (event) => this.handleMessage(event.data);
    socket.onclose = (event) => {
      if (this.socket !== socket) return;
      this.teardown();
      this.socket = null;
      if (this.closedByUser) return;
      // 4401 is the server's "token no longer valid" close code.
      if (event.code === 4401) {
        this.events.onState("unauthorized");
        return;
      }
      this.scheduleReconnect();
    };
    socket.onerror = () => socket.close();
  }

  private handleMessage(data: unknown): void {
    if (data instanceof ArrayBuffer) {
      this.events.onBinary?.(data);
      return;
    }
    if (typeof data !== "string") return;
    let message: { type?: string; paused?: boolean; code?: string; stream?: string; state?: string };
    try {
      message = JSON.parse(data) as { type?: string; paused?: boolean; code?: string; stream?: string; state?: string };
    } catch {
      return;
    }

    switch (message.type) {
      case "hello":
        this.attempt = 0;
        this.events.onState(message.paused ? "paused" : "connected");
        this.startPing();
        break;
      case "paused":
        this.events.onState(message.paused === false ? "connected" : "paused");
        break;
      case "pong":
        window.clearTimeout(this.pongTimer);
        break;
      case "error":
        if (message.code) this.events.onServerError?.(message.code);
        break;
      case "viewStatus":
        if (message.stream && message.state) this.events.onViewStatus?.(message.stream, message.state);
        break;
    }
  }

  private startPing(): void {
    window.clearInterval(this.pingTimer);
    this.pingTimer = window.setInterval(() => {
      if (this.socket?.readyState !== WebSocket.OPEN) return;
      this.socket.send(JSON.stringify({ type: "ping" }));
      window.clearTimeout(this.pongTimer);
      this.pongTimer = window.setTimeout(() => this.socket?.close(), PONG_TIMEOUT_MS);
    }, PING_INTERVAL_MS);
  }

  private scheduleReconnect(): void {
    this.events.onState("reconnecting");
    const delay = BACKOFF_MS[Math.min(this.attempt, BACKOFF_MS.length - 1)] ?? 8000;
    this.attempt++;
    this.reconnectTimer = window.setTimeout(() => this.connectNow(), delay);
  }

  private teardown(): void {
    window.clearInterval(this.pingTimer);
    window.clearTimeout(this.pongTimer);
    window.clearTimeout(this.reconnectTimer);
  }
}
