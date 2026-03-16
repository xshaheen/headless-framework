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
}

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
      if (!this.config?.enabled) {
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

      default:
        return null
    }
  }

  private async validateCredentials(): Promise<AuthStatus> {
    try {
      const authHeader = this.getAuthHeader()

      // Get the correct base URL with base path
      const config = window.MessagingConfig
      const baseUrl = config?.backendDomain || config?.basePath || '/messaging/dashboard'
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
    return !!(
      localStorage.getItem('messaging_basic_auth') ||
      localStorage.getItem('messaging_api_key') ||
      localStorage.getItem('messaging_host_access_key')
    )
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
    }
  }

  private clearCredentials(): void {
    localStorage.removeItem('messaging_basic_auth')
    localStorage.removeItem('messaging_api_key')
    localStorage.removeItem('messaging_host_access_key')
  }
}

// Export singleton instance
export const authService = new AuthService()
