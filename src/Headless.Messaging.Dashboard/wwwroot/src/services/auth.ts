/**
 * Authentication for the Messaging Dashboard.
 *
 * Basic and API key modes keep the credential in localStorage and send it as the Authorization header. Host mode keeps
 * nothing in the browser: the server's HttpOnly session cookie rides every same-origin request, and the session state
 * comes from api/auth/info.
 */

export type AuthMode = 'none' | 'basic' | 'apikey' | 'host'

export interface AuthConfig {
  mode: AuthMode
  enabled: boolean
  signInAvailable: boolean
}

export interface AuthStatus {
  authenticated: boolean
  username?: string
  message?: string
  canOperate: boolean
}

export interface LoginCredentials {
  username?: string
  password?: string
  apiKey?: string
}

/** The header the server requires on every state-changing request, so a cross-site form cannot make one. */
export const REQUESTED_WITH_HEADER = { 'X-Requested-With': 'XMLHttpRequest' } as const

const BASIC_KEY = 'messaging_basic_auth'
const API_KEY_KEY = 'messaging_api_key'
const CREDENTIAL_KEYS = [BASIC_KEY, API_KEY_KEY] as const

const DEFAULT_BASE_PATH = '/messaging/dashboard'

function getBaseUrl(): string {
  const config = window.MessagingConfig
  return (config?.backendDomain || config?.basePath || DEFAULT_BASE_PATH).replace(/\/+$/, '')
}

interface AuthInfo {
  authenticated: boolean
  name: string | null
  canOperate: boolean
}

function parseAuthInfo(value: unknown): AuthInfo | null {
  if (typeof value !== 'object' || value === null) return null
  const info = value as Record<string, unknown>
  if (typeof info.authenticated !== 'boolean' || typeof info.canOperate !== 'boolean') return null
  return {
    authenticated: info.authenticated,
    name: typeof info.name === 'string' ? info.name : null,
    canOperate: info.canOperate,
  }
}

class AuthService {
  private config: AuthConfig | null = null
  private status: AuthStatus = { authenticated: false, canOperate: false }

  async initialize(): Promise<void> {
    try {
      const windowConfig = window.MessagingConfig?.auth

      if (!windowConfig) {
        throw new Error('No auth configuration found')
      }

      this.config = {
        mode: windowConfig.mode,
        enabled: windowConfig.enabled,
        signInAvailable: windowConfig.signInAvailable === true,
      }

      if (!this.config.enabled) {
        this.status = { authenticated: true, username: 'anonymous', canOperate: true }
        return
      }

      if (this.config.mode === 'host') {
        this.status = await this.fetchHostSession()
        return
      }

      // Basic and API key share one credential for viewing and operating, so a signed-in user may operate.
      if (this.hasStoredCredentials()) {
        this.status = {
          authenticated: true,
          username: this.getStoredUsername() || 'user',
          canOperate: true,
        }
      } else {
        this.status = { authenticated: false, message: 'Please log in', canOperate: false }
      }
    } catch (error) {
      console.error('Auth initialization failed:', error)
      this.status = {
        authenticated: false,
        message: 'Authentication service unavailable',
        canOperate: false,
      }
    }
  }

  /** Signs in with a Basic or API key credential. Host mode signs in through {@link getSignInUrl} instead. */
  async login(credentials: LoginCredentials): Promise<boolean> {
    try {
      if (!this.config) {
        await this.initialize()
      }

      if (!this.config) {
        return false
      }

      if (!this.config.enabled) {
        return true
      }

      if (this.config.mode === 'host') {
        return false
      }

      this.storeCredentials(credentials)
      const result = await this.validateCredentials()
      this.status = result

      if (result.authenticated) {
        return true
      }

      this.clearCredentials()
      return false
    } catch (error) {
      console.error('Login failed:', error)
      this.status = { authenticated: false, message: 'Login failed', canOperate: false }
      return false
    }
  }

  /**
   * Ends the dashboard session. In host mode the server deletes the dashboard cookie; the host application's own
   * session stays, and nothing signs in again until the operator chooses Sign in.
   */
  async signOut(): Promise<void> {
    if (this.config?.mode === 'host') {
      try {
        await fetch(`${getBaseUrl()}/api/auth/sign-out`, {
          method: 'POST',
          credentials: 'same-origin',
          headers: { ...REQUESTED_WITH_HEADER },
        })
      } catch (error) {
        console.error('Sign-out failed:', error)
      }
    }

    this.clearSession()
  }

  /** Forgets the local session without calling the server, for a request the server already answered with 401. */
  clearSession(): void {
    this.clearCredentials()
    this.status = { authenticated: false, canOperate: false }
  }

  /**
   * The host-mode sign-in URL. It is a top-level navigation: the server signs the host user in, or challenges the
   * host's scheme, then redirects back to the return path, which must be a local path under the dashboard base.
   */
  getSignInUrl(returnPath: string = '/'): string {
    const base = (window.MessagingConfig?.basePath || DEFAULT_BASE_PATH).replace(/\/+$/, '')
    const path = returnPath.startsWith('/') ? returnPath : `/${returnPath}`
    return `${base}/auth/sign-in?returnUrl=${encodeURIComponent(`${base}${path}`)}`
  }

  getStatus(): AuthStatus {
    return { ...this.status }
  }

  getConfig(): AuthConfig | null {
    return this.config ? { ...this.config } : null
  }

  isAuthenticated(): boolean {
    return this.status.authenticated
  }

  /** The Authorization header for Basic and API key modes; none in host mode, where the cookie authenticates. */
  getAuthHeader(): string | null {
    const auth = window.MessagingConfig?.auth
    if (!auth?.enabled) {
      return null
    }

    switch (auth.mode) {
      case 'basic': {
        const basicAuth = localStorage.getItem(BASIC_KEY)
        return basicAuth ? `Basic ${basicAuth}` : null
      }

      case 'apikey': {
        const apiKey = localStorage.getItem(API_KEY_KEY)
        return apiKey ? `Bearer ${apiKey}` : null
      }

      default:
        return null
    }
  }

  private async fetchHostSession(): Promise<AuthStatus> {
    const response = await fetch(`${getBaseUrl()}/api/auth/info`, {
      method: 'GET',
      credentials: 'same-origin',
      headers: { ...REQUESTED_WITH_HEADER },
    })

    const info = response.ok ? parseAuthInfo(await response.json()) : null
    if (!info) {
      return {
        authenticated: false,
        message: 'Authentication service unavailable',
        canOperate: false,
      }
    }

    return info.authenticated
      ? { authenticated: true, username: info.name ?? undefined, canOperate: info.canOperate }
      : { authenticated: false, canOperate: false }
  }

  private async validateCredentials(): Promise<AuthStatus> {
    try {
      const authHeader = this.getAuthHeader()
      const controller = new AbortController()
      const timeoutId = setTimeout(() => controller.abort(), 5000)

      const response = await fetch(`${getBaseUrl()}/api/auth/validate`, {
        method: 'POST',
        headers: authHeader
          ? { ...REQUESTED_WITH_HEADER, Authorization: authHeader }
          : { ...REQUESTED_WITH_HEADER },
        signal: controller.signal,
      })

      clearTimeout(timeoutId)

      if (response.ok) {
        const result = await response.json()
        return {
          authenticated: result.authenticated === true,
          username: result.username,
          message: result.message,
          canOperate: result.authenticated === true,
        }
      }

      return {
        authenticated: false,
        message: `Server error: ${response.status}`,
        canOperate: false,
      }
    } catch (error) {
      console.error('Credential validation failed:', error)
      return { authenticated: false, message: 'Validation failed', canOperate: false }
    }
  }

  async validateStoredCredentials(): Promise<void> {
    const result = await this.validateCredentials()
    this.status = result

    if (!result.authenticated) {
      this.clearCredentials()
    }
  }

  private hasStoredCredentials(): boolean {
    return CREDENTIAL_KEYS.some((key) => !!localStorage.getItem(key))
  }

  private getStoredUsername(): string | null {
    const basicAuth = localStorage.getItem(BASIC_KEY)
    if (basicAuth) {
      try {
        return atob(basicAuth).split(':')[0] ?? null
      } catch {
        return null
      }
    }
    return null
  }

  private storeCredentials(credentials: LoginCredentials): void {
    this.clearCredentials()

    switch (this.config?.mode) {
      case 'basic':
        if (credentials.username && credentials.password) {
          localStorage.setItem(BASIC_KEY, btoa(`${credentials.username}:${credentials.password}`))
        }
        break

      case 'apikey':
        if (credentials.apiKey) {
          localStorage.setItem(API_KEY_KEY, credentials.apiKey)
        }
        break
    }
  }

  private clearCredentials(): void {
    for (const key of CREDENTIAL_KEYS) {
      localStorage.removeItem(key)
    }
  }
}

export const authService = new AuthService()
