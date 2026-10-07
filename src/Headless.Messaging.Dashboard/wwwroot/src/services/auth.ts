/**
 * Clean, simple authentication service for Messaging Dashboard
 */

export interface AuthConfig {
  mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom'
  enabled: boolean
  sessionTimeout: number
}

export interface AuthStatus {
  authenticated: boolean
  username?: string
  message?: string
}

export interface LoginCredentials {
  username?: string
  password?: string
  apiKey?: string
  hostAccessKey?: string
  customCredential?: string
}

export const SESSION_EXPIRED_MESSAGE = 'Session expired. Please log in again.'

/** Dispatched on window when an API request finds the stored sign-in older than the configured session timeout. */
export const SESSION_EXPIRED_EVENT = 'auth:session-expired'

const CREDENTIAL_KEYS = [
  'messaging_basic_auth',
  'messaging_api_key',
  'messaging_host_access_key',
  'messaging_custom_auth',
] as const

const SIGNED_IN_AT_KEY = 'messaging_signed_in_at'

class AuthService {
  private config: AuthConfig | null = null
  private status: AuthStatus = { authenticated: false }

  /**
   * Initialize authentication service
   */
  async initialize(): Promise<void> {
    try {
      // Get auth configuration from window (injected by backend)
      const windowConfig = window.MessagingConfig?.auth

      if (!windowConfig) {
        throw new Error('No auth configuration found')
      }

      this.config = {
        mode: windowConfig.mode as AuthConfig['mode'],
        enabled: windowConfig.enabled,
        sessionTimeout: windowConfig.sessionTimeout,
      }

      // If no auth required, set as authenticated
      if (!this.config.enabled) {
        this.status = { authenticated: true, username: 'anonymous' }
        return
      }

      // Check if we have stored credentials without validating
      if (this.hasStoredCredentials()) {
        const username = this.getStoredUsername()
        this.status = { authenticated: true, username: username || 'user' }
      } else {
        this.status = { authenticated: false, message: 'Please log in' }
      }
    } catch (error) {
      console.error('Auth initialization failed:', error)
      this.status = { authenticated: false, message: 'Authentication service unavailable' }
    }
  }

  /**
   * Login with credentials
   */
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

      // Store credentials based on auth mode
      this.storeCredentials(credentials)

      // Validate with backend
      const result = await this.validateCredentials()

      if (result.authenticated) {
        this.status = result
        return true
      }

      // Clear invalid credentials
      this.clearCredentials()
      this.status = result
      return false
    } catch (error) {
      console.error('Login failed:', error)
      this.status = { authenticated: false, message: 'Login failed' }
      return false
    }
  }

  /**
   * Logout
   */
  logout(): void {
    this.clearCredentials()
    this.status = { authenticated: false }
  }

  /**
   * Clears the stored credentials when the sign-in is older than the configured session timeout, so the next request
   * or navigation has to sign in again. Returns true when it expired the session.
   */
  expireSessionIfStale(now: number = Date.now()): boolean {
    if (!this.isSessionExpired(now)) {
      return false
    }

    this.clearCredentials()
    this.status = { authenticated: false, message: SESSION_EXPIRED_MESSAGE }
    return true
  }

  /**
   * Reads the configuration the server injected rather than the initialized copy, because a request can run before
   * initialize() does. Mode none and a non-positive timeout never expire.
   */
  private isSessionExpired(now: number): boolean {
    const auth = window.MessagingConfig?.auth
    if (!auth?.enabled || auth.mode === 'none') {
      return false
    }

    const timeoutMinutes = auth.sessionTimeout
    if (!Number.isFinite(timeoutMinutes) || timeoutMinutes <= 0) {
      return false
    }

    if (!this.hasStoredCredentials()) {
      return false
    }

    // Credentials without a sign-in time have no provable age, so they are treated as expired.
    const signedInAt = Number(localStorage.getItem(SIGNED_IN_AT_KEY))
    if (!Number.isFinite(signedInAt) || signedInAt <= 0) {
      return true
    }

    return now - signedInAt >= timeoutMinutes * 60_000
  }

  /**
   * Get current authentication status
   */
  getStatus(): AuthStatus {
    return { ...this.status }
  }

  /**
   * Get authentication configuration
   */
  getConfig(): AuthConfig | null {
    return this.config ? { ...this.config } : null
  }

  /**
   * Check if user is authenticated
   */
  isAuthenticated(): boolean {
    return this.status.authenticated
  }

  /**
   * Get authorization header for API calls
   */
  getAuthHeader(): string | null {
    if (!this.config?.enabled) {
      return null
    }

    switch (this.config.mode) {
      case 'basic': {
        const basicAuth = localStorage.getItem('messaging_basic_auth')
        return basicAuth ? `Basic ${basicAuth}` : null
      }

      case 'apikey': {
        const apiKey = localStorage.getItem('messaging_api_key')
        return apiKey ? `Bearer ${apiKey}` : null
      }

      case 'host': {
        const hostAccessKey = localStorage.getItem('messaging_host_access_key')
        return hostAccessKey || null
      }

      // The server's custom validator receives the raw Authorization value, so the credential goes out verbatim.
      case 'custom': {
        const customCredential = localStorage.getItem('messaging_custom_auth')
        return customCredential || null
      }

      default:
        return null
    }
  }

  private async validateCredentials(): Promise<AuthStatus> {
    try {
      const authHeader = this.getAuthHeader()

      // Get the correct base URL with base path
      const config = window.MessagingConfig
      const baseUrl = config?.backendDomain || config?.basePath || '/messaging'
      const url = `${baseUrl}/api/auth/validate`

      // Add timeout to prevent hanging
      const controller = new AbortController()
      const timeoutId = setTimeout(() => controller.abort(), 5000)

      const response = await fetch(url, {
        method: 'POST',
        headers: authHeader ? { Authorization: authHeader } : {},
        signal: controller.signal,
      })

      clearTimeout(timeoutId)

      if (response.ok) {
        const result = await response.json()
        return {
          authenticated: result.authenticated,
          username: result.username,
          message: result.message,
        }
      }

      return { authenticated: false, message: `Server error: ${response.status}` }
    } catch (error) {
      console.error('Credential validation failed:', error)
      return { authenticated: false, message: 'Validation failed' }
    }
  }

  /**
   * Validate stored credentials (public method)
   */
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
    const basicAuth = localStorage.getItem('messaging_basic_auth')
    if (basicAuth) {
      try {
        const decoded = atob(basicAuth)
        return decoded.split(':')[0]
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
          const encoded = btoa(`${credentials.username}:${credentials.password}`)
          localStorage.setItem('messaging_basic_auth', encoded)
        }
        break

      case 'apikey':
        if (credentials.apiKey) {
          localStorage.setItem('messaging_api_key', credentials.apiKey)
        }
        break

      case 'host':
        if (credentials.hostAccessKey) {
          localStorage.setItem('messaging_host_access_key', credentials.hostAccessKey)
        }
        break

      case 'custom':
        if (credentials.customCredential) {
          localStorage.setItem('messaging_custom_auth', credentials.customCredential)
        }
        break
    }

    if (this.hasStoredCredentials()) {
      localStorage.setItem(SIGNED_IN_AT_KEY, Date.now().toString())
    }
  }

  private clearCredentials(): void {
    for (const key of CREDENTIAL_KEYS) {
      localStorage.removeItem(key)
    }
    localStorage.removeItem(SIGNED_IN_AT_KEY)
  }
}

// Export singleton instance
export const authService = new AuthService()
