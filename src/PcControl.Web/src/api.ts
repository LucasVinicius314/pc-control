// REST client. The contract is documented in docs/protocol/openapi.yaml.

export interface ServerInfo {
  app: string;
  protocolVersion: number;
  paused: boolean;
}

export interface Session {
  deviceId: string;
  deviceName: string;
  paused: boolean;
}

export type Capabilities = Record<string, boolean>;

export interface Monitor {
  index: number;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  primary: boolean;
}

export interface DpiStatus {
  available: boolean;
  /** Why the toggle is off, when it is. */
  reason: string | null;
  percent: number | null;
  lowPercent: number;
  highPercent: number;
  monitor: { index: number; name: string } | null;
  backend: string | null;
}

export interface AppItem {
  id: string;
  name: string;
  favorite: boolean;
}

export interface AppsResponse {
  apps: AppItem[];
  /** Ids of recently launched apps, newest first. */
  recent: string[];
  launcher: { powerToysRunShortcut: string; powerToysDetected: boolean };
}

export interface ProcessItem {
  id: string;
  name: string;
  title: string;
}

export interface PendingPower {
  action: string;
  secondsLeft: number;
}

export interface DpiChange {
  previous: number;
  percent: number;
  monitor: { index: number; name: string } | null;
  note: string;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly retryAfterSeconds?: number,
    /** A sentence from the PC explaining the problem, when it sent one. */
    readonly detail?: string,
  ) {
    super(code);
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    credentials: "same-origin",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    let code = "http_" + response.status;
    let detail: string | undefined;
    try {
      const data = (await response.json()) as { error?: string; message?: string };
      if (data.error) code = data.error;
      if (data.message) detail = data.message;
    } catch {
      // Body was not JSON; keep the status-based code.
    }
    const retry = Number(response.headers.get("Retry-After"));
    throw new ApiError(response.status, code, Number.isFinite(retry) && retry > 0 ? retry : undefined, detail);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  info: () => request<ServerInfo>("GET", "/api/info"),
  session: () => request<Session>("GET", "/api/session"),
  capabilities: () => request<Capabilities>("GET", "/api/capabilities"),
  monitors: () => request<Monitor[]>("GET", "/api/monitors"),
  dpi: () => request<DpiStatus>("GET", "/api/dpi"),
  toggleDpi: () => request<DpiChange>("POST", "/api/dpi/toggle"),
  apps: () => request<AppsResponse>("GET", "/api/apps"),
  launchApp: (id: string) => request<{ launched: boolean; name: string }>("POST", `/api/apps/${id}/launch`),
  setFavorite: (id: string, favorite: boolean) => request<void>("PUT", `/api/apps/${id}/favorite`, { favorite }),
  powerToysRun: () => request<void>("POST", "/api/powertoys-run"),
  openUrl: (url: string) => request<{ opened: boolean; url: string }>("POST", "/api/url", { url }),
  powerStatus: () => request<{ pending: PendingPower | null }>("GET", "/api/power"),
  power: (action: string, confirm: boolean) =>
    request<{ action: string; delaySeconds: number }>("POST", "/api/power", { action, confirm }),
  cancelPower: () => request<void>("DELETE", "/api/power/pending"),
  processes: () => request<ProcessItem[]>("GET", "/api/processes"),
  focusProcess: (id: string) => request<{ focused: boolean; title: string }>("POST", `/api/processes/${id}/focus`),
  closeProcess: (id: string, force: boolean) =>
    request<{ closed: boolean; title: string }>("POST", `/api/processes/${id}/close`, { force }),
  pair: (pin: string, deviceName: string) =>
    request<Session>("POST", "/api/pair", { pin, deviceName }),
  unpair: () => request<void>("DELETE", "/api/session"),
};
