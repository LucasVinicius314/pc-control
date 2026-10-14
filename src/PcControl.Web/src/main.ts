import "./style.css";
import { api, ApiError, type Capabilities, type Session } from "./api";
import { Link, type LinkState } from "./socket";

const root = document.getElementById("app") as HTMLElement;

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: Partial<HTMLElementTagNameMap[K]> & { class?: string } = {},
  ...children: (Node | string)[]
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  const { class: className, ...rest } = props;
  if (className) node.className = className;
  Object.assign(node, rest);
  node.append(...children);
  return node;
}

function render(...nodes: Node[]): void {
  root.replaceChildren(...nodes);
}

function guessDeviceName(): string {
  const ua = navigator.userAgent;
  const model = /Android[^;)]*;\s*([^;)]+?)(?:\sBuild|\))/.exec(ua)?.[1]?.trim();
  return model && model !== "K" ? model : /Android/.test(ua) ? "Android phone" : "Phone";
}

// A QR code or link may carry the PIN in the URL fragment (#pin=123456). Fragments are never
// sent to the server, so the PIN does not end up in request logs.
function takePinFromFragment(): string | null {
  const match = /(?:^|[#&])pin=(\d{6})(?:&|$)/.exec(location.hash);
  if (!match) return null;
  history.replaceState(null, "", location.pathname + location.search);
  return match[1] ?? null;
}

function describePairError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) {
      const minutes = Math.ceil((error.retryAfterSeconds ?? 60) / 60);
      return `Too many wrong attempts. Try again in about ${minutes} min.`;
    }
    if (error.code === "invalid_pin") return "That PIN is wrong or expired. Show a new one on the PC.";
    if (error.code === "forbidden") return "This PC refused the request.";
  }
  return "Could not reach the PC. Check that you are on the same Wi-Fi.";
}

function showPairing(initialPin: string | null, notice = ""): void {
  const pinInput = el("input", {
    class: "pin",
    inputMode: "numeric",
    autocomplete: "one-time-code",
    maxLength: 6,
    placeholder: "000000",
    ariaLabel: "Pairing PIN",
  });
  const nameInput = el("input", { value: guessDeviceName(), maxLength: 64, autocomplete: "off" });
  const error = el("p", { class: "error", textContent: notice });
  const button = el("button", { type: "submit", textContent: "Pair" });

  const submit = async (event?: Event): Promise<void> => {
    event?.preventDefault();
    if (!/^\d{6}$/.test(pinInput.value)) {
      error.textContent = "Enter the 6-digit PIN shown on the PC.";
      return;
    }
    button.disabled = true;
    error.textContent = "";
    try {
      const session = await api.pair(pinInput.value, nameInput.value);
      await showConnected(session);
    } catch (e) {
      error.textContent = describePairError(e);
      button.disabled = false;
    }
  };

  const form = el(
    "form",
    { class: "card", onsubmit: submit },
    el("h1", {}, "Pair with your PC"),
    el("p", {}, "On the PC, open the PC Control tray icon and choose “Show pairing PIN”."),
    el("label", {}, "PIN", pinInput),
    el("label", {}, "Name of this phone", nameInput),
    error,
    button,
  );

  render(form);
  pinInput.value = initialPin ?? "";
  if (initialPin) void submit();
}

async function showConnected(session: Session): Promise<void> {
  const dot = el("span", { class: "dot" });
  const label = el("span", {}, "Connecting…");
  const detail = el("p", { class: "meta" });
  let capabilities: Capabilities = {};

  const states: Record<LinkState, [string, string]> = {
    connecting: ["warn", "Connecting…"],
    connected: ["ok", "Connected"],
    paused: ["warn", "Paused on the PC"],
    reconnecting: ["warn", "Reconnecting…"],
    unauthorized: ["bad", "Not paired anymore"],
  };

  const link = new Link({
    onState: (state) => {
      const [kind, text] = states[state];
      dot.className = "dot " + kind;
      label.textContent = text;
      if (state === "unauthorized") {
        link.close();
        showPairing(null, "This phone was removed on the PC. Pair again.");
      }
    },
  });

  const unpair = el("button", {
    class: "secondary",
    textContent: "Forget this PC",
    onclick: async () => {
      link.close();
      try {
        await api.unpair();
      } finally {
        showPairing(null);
      }
    },
  });

  render(
    el(
      "section",
      { class: "card" },
      el("h1", {}, "PC Control"),
      el("div", { class: "status" }, dot, label),
      detail,
      unpair,
    ),
  );

  link.connect();

  try {
    capabilities = await api.capabilities();
    const on = Object.values(capabilities).filter(Boolean).length;
    detail.textContent = `${session.deviceName} · ${on} of ${Object.keys(capabilities).length} features available`;
  } catch {
    detail.textContent = session.deviceName;
  }
}

async function start(): Promise<void> {
  const pin = takePinFromFragment();
  try {
    await api.info();
  } catch {
    showPairing(pin, "Could not reach the PC. Check that you are on the same Wi-Fi.");
    return;
  }

  if (!pin) {
    try {
      await showConnected(await api.session());
      return;
    } catch {
      // Not paired yet, or the token was revoked.
    }
  }
  showPairing(pin);
}

void start();
