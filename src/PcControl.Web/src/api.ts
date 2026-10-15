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

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly retryAfterSeconds?: number,
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
    try {
      const data = (await response.json()) as { error?: string };
      if (data.error) code = data.error;
    } catch {
      // Body was not JSON; keep the status-based code.
    }
    const retry = Number(response.headers.get("Retry-After"));
    throw new ApiError(response.status, code, Number.isFinite(retry) && retry > 0 ? retry : undefined);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  info: () => request<ServerInfo>("GET", "/api/info"),
  session: () => request<Session>("GET", "/api/session"),
  capabilities: () => request<Capabilities>("GET", "/api/capabilities"),
  monitors: () => request<Monitor[]>("GET", "/api/monitors"),
  pair: (pin: string, deviceName: string) =>
    request<Session>("POST", "/api/pair", { pin, deviceName }),
  unpair: () => request<void>("DELETE", "/api/session"),
};
