/**
 * Clean, simple authentication service for Jobs Dashboard
 */

// Extend window interface for TypeScript
declare global {
  interface Window {
    JobsConfig?: {
      basePath: string;
      backendDomain?: string;
      auth: {
        mode: string;
        enabled: boolean;
        sessionTimeout: number;
      };
    };
  }
}

export interface AuthConfig {
  mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom';
  enabled: boolean;
  sessionTimeout: number;
}

export interface AuthStatus {
  authenticated: boolean;
  username?: string;
  message?: string;
}

export interface LoginCredentials {
  username?: string;
  password?: string;
  apiKey?: string;
  hostAccessKey?: string;
  customCredential?: string;
}

const BASIC_AUTH_KEY = 'jobs_basic_auth';
const API_KEY_KEY = 'jobs_api_key';
const HOST_ACCESS_KEY_KEY = 'jobs_host_access_key';
// Custom mode hands the Authorization header value to the host's validator untouched, so the credential is stored
// and sent verbatim with no scheme prefix.
const CUSTOM_CREDENTIAL_KEY = 'jobs_custom_credential';
const SIGNED_IN_AT_KEY = 'jobs_signed_in_at';

export const SESSION_EXPIRED_MESSAGE = 'Session expired. Please log in again.';

const CREDENTIAL_KEYS = [BASIC_AUTH_KEY, API_KEY_KEY, HOST_ACCESS_KEY_KEY, CUSTOM_CREDENTIAL_KEY];

const hasAnyStoredCredential = (): boolean => CREDENTIAL_KEYS.some((key) => !!localStorage.getItem(key));

/**
 * Whether the stored sign-in is older than the server's session timeout (window.JobsConfig.auth.sessionTimeout, in
 * minutes). Reads the injected config directly so it works before the auth service is initialized, which is when
 * the first API requests can fire. Mode none, disabled auth, and a non-positive timeout never expire. Stored
 * credentials without a readable sign-in time count as expired, so a session of unknown age is not trusted.
 */
export function isSessionExpired(now: number = Date.now()): boolean {
  const auth = window.JobsConfig?.auth;
  if (!auth || !auth.enabled || auth.mode === 'none') {
    return false;
  }

  const timeoutMinutes = Number(auth.sessionTimeout);
  if (!Number.isFinite(timeoutMinutes) || timeoutMinutes <= 0) {
    return false;
  }

  if (!hasAnyStoredCredential()) {
    return false;
  }

  const signedInAt = Number(localStorage.getItem(SIGNED_IN_AT_KEY));
  if (!Number.isFinite(signedInAt) || signedInAt <= 0) {
    return true;
  }

  return now - signedInAt >= timeoutMinutes * 60_000;
}

/**
 * The credential the notification hub sends as its access_token query parameter, since a browser cannot set headers
 * on a WebSocket. Reads the injected config directly, because the hub connection is built when its module loads,
 * before the auth service is initialized. Each value is what the server accepts for the mode: the Base64 Basic
 * credential, `Bearer:<key>` for an API key, and the host access key or custom credential verbatim.
 */
export function getHubAccessToken(): string | null {
  const auth = window.JobsConfig?.auth;
  if (!auth?.enabled) {
    return null;
  }

  switch (auth.mode) {
    case 'basic':
      return localStorage.getItem(BASIC_AUTH_KEY) || null;

    case 'apikey': {
      const apiKey = localStorage.getItem(API_KEY_KEY);
      return apiKey ? `Bearer:${apiKey}` : null;
    }

    case 'host':
      return localStorage.getItem(HOST_ACCESS_KEY_KEY) || null;

    case 'custom':
      return localStorage.getItem(CUSTOM_CREDENTIAL_KEY) || null;

    default:
      return null;
  }
}

class AuthService {
  private config: AuthConfig | null = null;
  private status: AuthStatus = { authenticated: false };

  /**
   * Initialize authentication service
   */
  async initialize(): Promise<void> {
    try {
      // Get auth configuration from window (injected by backend)
      const windowConfig = window.JobsConfig?.auth;
      
      if (!windowConfig) {
        throw new Error('No auth configuration found');
      }
      
      this.config = {
        mode: windowConfig.mode as 'none' | 'basic' | 'apikey' | 'host' | 'custom',
        enabled: windowConfig.enabled,
        sessionTimeout: windowConfig.sessionTimeout
      };
      
      // If no auth required, set as authenticated
      if (!this.config.enabled) {
        this.status = { authenticated: true, username: 'anonymous' };
        return;
      }

      // For now, just check if we have stored credentials without validating
      // This prevents hanging on API calls during initialization
      if (this.hasStoredCredentials()) {
        // Set as authenticated for now - validation will happen on first API call
        const username = this.getStoredUsername();
        this.status = { authenticated: true, username: username || 'user' };
      } else {
        // No stored credentials - user needs to log in
        this.status = { authenticated: false, message: 'Please log in' };
      }
    } catch (error) {
      console.error('Auth initialization failed:', error);
      this.status = { authenticated: false, message: 'Authentication service unavailable' };
    }
  }

  /**
   * Login with credentials
   */
  async login(credentials: LoginCredentials): Promise<boolean> {
    try {
      if (!this.config) {
        await this.initialize();
      }

      if (!this.config) {
        return false;
      }

      if (!this.config.enabled) {
        return true;
      }

      // Store credentials based on auth mode
      this.storeCredentials(credentials);

      // Validate with backend
      const result = await this.validateCredentials();
      
      if (result.authenticated) {
        this.status = result;
        return true;
      }

      // Clear invalid credentials
      this.clearCredentials();
      this.status = result;
      return false;
    } catch (error) {
      console.error('Login failed:', error);
      this.status = { authenticated: false, message: 'Login failed' };
      return false;
    }
  }

  /**
   * Logout
   */
  logout(): void {
    this.clearCredentials();
    this.status = { authenticated: false };
  }

  /**
   * Clears stored credentials when the session has outlived the configured timeout.
   * @returns true when the session was expired and cleared.
   */
  expireStaleSession(now: number = Date.now()): boolean {
    if (!isSessionExpired(now)) {
      return false;
    }

    this.clearCredentials();
    this.status = { authenticated: false, message: SESSION_EXPIRED_MESSAGE };
    return true;
  }

  /**
   * Get current authentication status
   */
  getStatus(): AuthStatus {
    return { ...this.status };
  }

  /**
   * Get authentication configuration
   */
  getConfig(): AuthConfig | null {
    return this.config ? { ...this.config } : null;
  }

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean {
    return this.status.authenticated;
  }

  /**
   * Get authorization header for API calls
   */
  getAuthHeader(): string | null {
    if (!this.config?.enabled) {
      return null;
    }

    switch (this.config.mode) {
      case 'basic':
        const basicAuth = localStorage.getItem('jobs_basic_auth');
        return basicAuth ? `Basic ${basicAuth}` : null;
      
      case 'apikey':
        const apiKey = localStorage.getItem('jobs_api_key');
        return apiKey ? `Bearer ${apiKey}` : null;
      
      case 'host':
        const hostAccessKey = localStorage.getItem('jobs_host_access_key');
        return hostAccessKey || null;

      case 'custom':
        return localStorage.getItem(CUSTOM_CREDENTIAL_KEY) || null;
      
      default:
        return null;
    }
  }

  /**
   * Get access token for SignalR (WebSocket limitation)
   */
  getAccessToken(): string | null {
    return getHubAccessToken();
  }


  private async validateCredentials(): Promise<AuthStatus> {
    try {
      const authHeader = this.getAuthHeader();
      
      // Get the correct base URL with base path
      const config = window.JobsConfig;
      const baseUrl = config?.backendDomain || config?.basePath || '/jobs/dashboard';
      const url = `${baseUrl}/api/auth/validate`;
      
      // Add timeout to prevent hanging
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 5000); // 5 second timeout
      
      const response = await fetch(url, {
        method: 'POST',
        headers: authHeader ? { 'Authorization': authHeader } : {},
        signal: controller.signal
      });
      
      clearTimeout(timeoutId);

      if (response.ok) {
        const result = await response.json();
        return {
          authenticated: result.authenticated,
          username: result.username,
          message: result.message
        };
      }

      return { authenticated: false, message: `Server error: ${response.status}` };
    } catch (error) {
      console.error('Credential validation failed:', error);
      return { authenticated: false, message: 'Validation failed' };
    }
  }

  /**
   * Validate stored credentials (public method)
   */
  async validateStoredCredentials(): Promise<void> {
    const result = await this.validateCredentials();
    this.status = result;
    
    if (!result.authenticated) {
      this.clearCredentials();
    }
  }

  private hasStoredCredentials(): boolean {
    return hasAnyStoredCredential();
  }

  private getStoredUsername(): string | null {
    const basicAuth = localStorage.getItem('jobs_basic_auth');
    if (basicAuth) {
      try {
        const decoded = atob(basicAuth);
        return decoded.split(':')[0];
      } catch {
        return null;
      }
    }
    return null;
  }

  private storeCredentials(credentials: LoginCredentials): void {
    this.clearCredentials();

    switch (this.config?.mode) {
      case 'basic':
        if (credentials.username && credentials.password) {
          const encoded = btoa(`${credentials.username}:${credentials.password}`);
          localStorage.setItem('jobs_basic_auth', encoded);
        }
        break;
      
      case 'apikey':
        if (credentials.apiKey) {
          localStorage.setItem('jobs_api_key', credentials.apiKey);
        }
        break;
      
      case 'host':
        if (credentials.hostAccessKey) {
          localStorage.setItem('jobs_host_access_key', credentials.hostAccessKey);
        }
        break;

      case 'custom':
        if (credentials.customCredential) {
          localStorage.setItem(CUSTOM_CREDENTIAL_KEY, credentials.customCredential);
        }
        break;
    }

    // The session timeout counts from here, the moment the credential is stored.
    if (hasAnyStoredCredential()) {
      localStorage.setItem(SIGNED_IN_AT_KEY, String(Date.now()));
    }
  }

  private clearCredentials(): void {
    for (const key of CREDENTIAL_KEYS) {
      localStorage.removeItem(key);
    }
    localStorage.removeItem(SIGNED_IN_AT_KEY);
  }
}

// Export singleton instance
export const authService = new AuthService();
