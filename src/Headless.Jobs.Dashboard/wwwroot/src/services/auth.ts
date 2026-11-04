/**
 * Authentication service for the Jobs Dashboard.
 *
 * Basic and API key modes keep the credential in localStorage and send it on every request. Host mode keeps nothing
 * in the browser: the server issues an HttpOnly session cookie from the host's own sign-in, the browser sends it on
 * every same-origin request, and the session state comes from `api/auth/info`.
 */

import type { JobsAuthMode } from '@/types/jobs-config';
import { getApiBaseUrl, getAuthMode } from '@/utilities/pathResolver';

export interface AuthStatus {
  authenticated: boolean;
  username?: string;
  message?: string;
  /** Whether the signed-in user may run write actions. Only host mode can sign in a user who may only view. */
  canOperate: boolean;
}

export interface LoginCredentials {
  username?: string;
  password?: string;
  apiKey?: string;
}

/** The server's `GET api/auth/info` answer. */
interface SessionInfo {
  authenticated: boolean;
  name: string | null;
  canOperate: boolean;
}

const BASIC_AUTH_KEY = 'jobs_basic_auth';
const API_KEY_KEY = 'jobs_api_key';

const CREDENTIAL_KEYS = [BASIC_AUTH_KEY, API_KEY_KEY];

// The server refuses a state-changing request without this header: a cross-site form cannot send it.
const REQUESTED_WITH = { 'X-Requested-With': 'XMLHttpRequest' };

export const SESSION_EXPIRED_MESSAGE = 'Your session has ended. Please sign in again.';

const isAuthEnabled = (): boolean => !!window.JobsConfig?.auth?.enabled;

/**
 * The Authorization header value for API calls, or null when the mode sends none. Reads the injected config directly
 * so it works before the auth service is initialized, which is when the first API requests can fire.
 */
export function getAuthorizationHeader(): string | null {
  if (!isAuthEnabled()) {
    return null;
  }

  switch (getAuthMode()) {
    case 'basic': {
      const basicAuth = localStorage.getItem(BASIC_AUTH_KEY);
      return basicAuth ? `Basic ${basicAuth}` : null;
    }

    case 'apikey': {
      const apiKey = localStorage.getItem(API_KEY_KEY);
      return apiKey ? `Bearer ${apiKey}` : null;
    }

    default:
      return null;
  }
}

/**
 * The credential the notification hub sends as its access_token query parameter, since a browser cannot set headers
 * on a WebSocket. Each value is what the server accepts for the mode: the Base64 Basic credential, and
 * `Bearer:<key>` for an API key. Host mode sends none, because the session cookie rides the WebSocket handshake.
 */
export function getHubAccessToken(): string | null {
  if (!isAuthEnabled()) {
    return null;
  }

  switch (getAuthMode()) {
    case 'basic':
      return localStorage.getItem(BASIC_AUTH_KEY) || null;

    case 'apikey': {
      const apiKey = localStorage.getItem(API_KEY_KEY);
      return apiKey ? `Bearer:${apiKey}` : null;
    }

    default:
      return null;
  }
}

class AuthService {
  private mode: JobsAuthMode = 'none';
  private status: AuthStatus = { authenticated: false, canOperate: false };

  async initialize(): Promise<void> {
    const windowConfig = window.JobsConfig?.auth;
    if (!windowConfig) {
      console.error('Auth initialization failed: no auth configuration found');
      this.status = { authenticated: false, canOperate: false, message: 'Authentication service unavailable' };
      return;
    }

    this.mode = windowConfig.mode;

    if (!windowConfig.enabled || this.mode === 'none') {
      this.status = { authenticated: true, username: 'anonymous', canOperate: true };
      return;
    }

    if (this.mode === 'host') {
      this.status = await this.fetchSession();
      return;
    }

    // Basic and API key: trust the stored credential until the first API call, which answers 401 when it is wrong.
    // The server applies no separate operate policy in these modes, so a signed-in user may operate.
    if (this.hasStoredCredentials()) {
      this.status = { authenticated: true, username: this.getStoredUsername() || 'user', canOperate: true };
    } else {
      this.status = { authenticated: false, canOperate: false };
    }
  }

  /** Signs in with a Basic or API key credential. Host mode signs in through the host's sign-in page instead. */
  async login(credentials: LoginCredentials): Promise<boolean> {
    const config = window.JobsConfig?.auth;
    if (!config) {
      this.status = { authenticated: false, canOperate: false, message: 'Authentication service unavailable' };
      return false;
    }

    this.mode = config.mode;
    if (!config.enabled) {
      return true;
    }

    if (this.mode !== 'basic' && this.mode !== 'apikey') {
      return false;
    }

    this.storeCredentials(credentials);
    const result = await this.validateCredentials();
    this.status = result;

    if (!result.authenticated) {
      this.clearCredentials();
      return false;
    }

    return true;
  }

  /**
   * Ends the dashboard session. In host mode this deletes the dashboard's session cookie on the server; the host's
   * own session stays, and nothing signs in again until the user chooses to.
   */
  async logout(): Promise<void> {
    this.clearCredentials();
    this.status = { authenticated: false, canOperate: false };

    if (this.mode === 'host') {
      try {
        await fetch(`${getApiBaseUrl()}/auth/sign-out`, { method: 'POST', headers: { ...REQUESTED_WITH } });
      } catch (error) {
        console.error('Sign-out failed:', error);
      }
    }
  }

  /** Forgets the session after the server answered 401, without another round trip. */
  expire(): void {
    this.clearCredentials();
    this.status = { authenticated: false, canOperate: false, message: SESSION_EXPIRED_MESSAGE };
  }

  getStatus(): AuthStatus {
    return { ...this.status };
  }

  isAuthenticated(): boolean {
    return this.status.authenticated;
  }

  private async fetchSession(): Promise<AuthStatus> {
    try {
      const response = await fetch(`${getApiBaseUrl()}/auth/info`, { headers: { Accept: 'application/json' } });
      if (!response.ok) {
        return { authenticated: false, canOperate: false, message: `Server error: ${response.status}` };
      }

      const info = (await response.json()) as Partial<SessionInfo>;
      if (info.authenticated !== true) {
        return { authenticated: false, canOperate: false };
      }

      return {
        authenticated: true,
        username: typeof info.name === 'string' && info.name ? info.name : 'user',
        canOperate: info.canOperate === true,
      };
    } catch (error) {
      console.error('Session lookup failed:', error);
      return { authenticated: false, canOperate: false, message: 'Authentication service unavailable' };
    }
  }

  private async validateCredentials(): Promise<AuthStatus> {
    try {
      const authHeader = getAuthorizationHeader();

      // Add timeout to prevent hanging
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 5000);

      const response = await fetch(`${getApiBaseUrl()}/auth/validate`, {
        method: 'POST',
        headers: authHeader ? { Authorization: authHeader } : {},
        signal: controller.signal,
      });

      clearTimeout(timeoutId);

      if (response.ok) {
        const result = await response.json();
        return {
          authenticated: result.authenticated,
          username: result.username,
          message: result.message,
          canOperate: result.authenticated === true,
        };
      }

      return { authenticated: false, canOperate: false, message: `Server error: ${response.status}` };
    } catch (error) {
      console.error('Credential validation failed:', error);
      return { authenticated: false, canOperate: false, message: 'Validation failed' };
    }
  }

  private hasStoredCredentials(): boolean {
    switch (this.mode) {
      case 'basic':
        return !!localStorage.getItem(BASIC_AUTH_KEY);
      case 'apikey':
        return !!localStorage.getItem(API_KEY_KEY);
      default:
        return false;
    }
  }

  private getStoredUsername(): string | null {
    const basicAuth = localStorage.getItem(BASIC_AUTH_KEY);
    if (basicAuth) {
      try {
        return atob(basicAuth).split(':')[0] ?? null;
      } catch {
        return null;
      }
    }
    return null;
  }

  private storeCredentials(credentials: LoginCredentials): void {
    this.clearCredentials();

    switch (this.mode) {
      case 'basic':
        if (credentials.username && credentials.password) {
          localStorage.setItem(BASIC_AUTH_KEY, btoa(`${credentials.username}:${credentials.password}`));
        }
        break;

      case 'apikey':
        if (credentials.apiKey) {
          localStorage.setItem(API_KEY_KEY, credentials.apiKey);
        }
        break;
    }
  }

  private clearCredentials(): void {
    for (const key of CREDENTIAL_KEYS) {
      localStorage.removeItem(key);
    }
  }
}

export const authService = new AuthService();
