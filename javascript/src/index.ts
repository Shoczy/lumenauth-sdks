/**
 * @lumenauth/client — the official JavaScript SDK for LumenAuth.
 *
 *   import { createClient } from "@lumenauth/client";
 *
 *   const auth = createClient({
 *     name:    "My app",
 *     ownerid: "aBcD3fGh1J",   // from your LumenAuth dashboard
 *     version: "1.0",
 *     secret:  "lumsk_…",      // app secret from Manage Apps
 *     hwid:    getMachineId(), // optional device lock
 *   });
 *
 *   await auth.init();          // resolves the app + checks the version
 *   await auth.register({ email, password, license });
 *   if (await auth.check()) startApp();
 *   const bytes = await auth.file(fileId);
 *
 * No URL and no secret go in your code — the app is bound by name + ownerid +
 * version, and init() mints a hidden, short-lived session the SDK manages.
 */

const DEFAULT_URL = "https://api.lumenauth.dev";

export type LumenUser = {
  id: string;
  email: string;
  level: number;
  expiresAt: string | null;
  createdAt: string;
};

export type AuthResult = {
  success: true;
  token: string;
  session: string;
  user: LumenUser;
};

export interface KeyValueStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export interface LumenAuthOptions {
  /** Application name (Manage Apps). */
  name: string;
  /** Your fixed account owner id (Manage Apps). */
  ownerid: string;
  /** App version; a mismatch is rejected at init (force-update). */
  version: string;
  /** App secret from Manage Apps; verified at init. */
  secret: string;
  /** Optional hardware id, sent with every auth call for device locking. */
  hwid?: string;
  /** Where to persist the user session. Defaults to in-memory. */
  storage?: KeyValueStorage;
  /** Override the API URL (self-hosting / local dev). Normally omit this. */
  url?: string;
}

export class LumenError extends Error {
  status?: number;
  constructor(message: string, status?: number) {
    super(message);
    this.name = "LumenError";
    this.status = status;
  }
}

const SESSION_KEY = "lumenauth.session";
const TOKEN_KEY = "lumenauth.token";

export class LumenAuthClient {
  private baseUrl: string;
  private name: string;
  private ownerid: string;
  private version: string;
  private secret: string;
  private hwid?: string;
  private storage?: KeyValueStorage;

  /** The hidden app-session from init(). */
  private appSession: string | null = null;

  private _session: string | null = null;
  private _token: string | null = null;
  user: LumenUser | null = null;

  constructor(options: LumenAuthOptions) {
    if (!options?.name) throw new LumenError("name is required");
    if (!options?.ownerid) throw new LumenError("ownerid is required");
    if (!options?.version) throw new LumenError("version is required");
    if (!options?.secret) throw new LumenError("secret is required");
    this.baseUrl = (options.url ?? DEFAULT_URL).replace(/\/+$/, "");
    this.name = options.name;
    this.ownerid = options.ownerid;
    this.version = options.version;
    this.secret = options.secret;
    this.hwid = options.hwid;
    this.storage = options.storage;
    this._session = this.storage?.getItem(SESSION_KEY) ?? null;
    this._token = this.storage?.getItem(TOKEN_KEY) ?? null;
  }

  get session(): string | null {
    return this._session;
  }
  get token(): string | null {
    return this._token;
  }

  /** Resolve the app and check the version. Call once at startup. */
  async init(): Promise<void> {
    const res = await this.post<{ success: true; session: string }>(
      "/api/1.x/init",
      {
        name: this.name,
        ownerid: this.ownerid,
        version: this.version,
        secret: this.secret,
      },
      false,
    );
    this.appSession = res.session;
  }

  private async ensureInit(): Promise<void> {
    if (!this.appSession) await this.init();
  }

  private persist(res: AuthResult) {
    this._session = res.session;
    this._token = res.token;
    this.user = res.user;
    this.storage?.setItem(SESSION_KEY, res.session);
    this.storage?.setItem(TOKEN_KEY, res.token);
  }

  async register(input: {
    email: string;
    password: string;
    license: string;
    hwid?: string;
  }): Promise<AuthResult> {
    await this.ensureInit();
    const res = await this.post<AuthResult>("/api/1.x/register", {
      email: input.email,
      password: input.password,
      license: input.license,
      hwid: input.hwid ?? this.hwid,
    });
    this.persist(res);
    return res;
  }

  async login(input: {
    email: string;
    password: string;
    hwid?: string;
  }): Promise<AuthResult> {
    await this.ensureInit();
    const res = await this.post<AuthResult>("/api/1.x/login", {
      email: input.email,
      password: input.password,
      hwid: input.hwid ?? this.hwid,
    });
    this.persist(res);
    return res;
  }

  async license(input: { license: string; hwid?: string }): Promise<AuthResult> {
    await this.ensureInit();
    const res = await this.post<AuthResult>("/api/1.x/license", {
      license: input.license,
      hwid: input.hwid ?? this.hwid,
    });
    this.persist(res);
    return res;
  }

  async check(): Promise<boolean> {
    if (!this._session) return false;
    try {
      await this.ensureInit();
      const res = await this.post<{ success: true; user: LumenUser }>(
        "/api/1.x/check",
        { session: this._session },
      );
      this.user = res.user;
      return true;
    } catch (err) {
      if (err instanceof LumenError && err.status && err.status < 500) {
        this.logout();
        return false;
      }
      throw err;
    }
  }

  async file(fileId: string): Promise<ArrayBuffer> {
    if (!this._session) throw new LumenError("Not logged in", 401);
    await this.ensureInit();
    let res: Response;
    try {
      res = await fetch(this.baseUrl + "/api/1.x/file", {
        method: "POST",
        headers: this.headers(),
        body: JSON.stringify({ session: this._session, fileId }),
      });
    } catch {
      throw new LumenError(`Could not reach LumenAuth at ${this.baseUrl}`);
    }
    if (!res.ok) {
      const data = (await res.json().catch(() => ({}))) as { message?: string };
      throw new LumenError(data.message ?? `Download failed (${res.status})`, res.status);
    }
    return res.arrayBuffer();
  }

  /**
   * Read a level-gated server variable by name. The value is only returned if
   * the logged-in user's subscription level is high enough, so you can keep
   * secrets (download URLs, config, feature flags) off the client.
   */
  async var(name: string): Promise<string> {
    if (!this._session) throw new LumenError("Not logged in", 401);
    await this.ensureInit();
    const res = await this.post<{ success: true; name: string; value: string }>(
      "/api/1.x/var",
      { session: this._session, name },
    );
    return res.value;
  }

  logout() {
    this._session = null;
    this._token = null;
    this.user = null;
    this.storage?.removeItem(SESSION_KEY);
    this.storage?.removeItem(TOKEN_KEY);
  }

  private headers(authed = true): Record<string, string> {
    const h: Record<string, string> = { "Content-Type": "application/json" };
    if (authed && this.appSession) h.Authorization = `Bearer ${this.appSession}`;
    return h;
  }

  private async post<T>(
    path: string,
    body: Record<string, unknown>,
    authed = true,
  ): Promise<T> {
    let res: Response;
    try {
      res = await fetch(this.baseUrl + path, {
        method: "POST",
        headers: this.headers(authed),
        body: JSON.stringify(body),
      });
    } catch {
      throw new LumenError(`Could not reach LumenAuth at ${this.baseUrl}`);
    }
    const data = (await res.json().catch(() => ({}))) as {
      message?: string;
      error?: string;
    };
    if (!res.ok) {
      throw new LumenError(
        data.message ?? data.error ?? `Request failed (${res.status})`,
        res.status,
      );
    }
    return data as T;
  }
}

export function createClient(options: LumenAuthOptions): LumenAuthClient {
  return new LumenAuthClient(options);
}
