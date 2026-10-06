/**
 * Utility functions for resolving paths using the Messaging configuration
 */

/**
 * Resolve a path relative to the Messaging base path
 */
export function resolvePath(path: string): string {
  const config = window.MessagingConfig

  if (!config) {
    console.warn('Messaging configuration not found, using path as-is')
    return path
  }

  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path
  }

  if (path.startsWith('/')) {
    return `${config.basePath}${path}`
  }

  return `${config.basePath}/${path}`
}

/**
 * Resolve an API URL using the backend domain or base path + /api
 */
export function resolveApiUrl(endpoint: string): string {
  const config = window.MessagingConfig

  if (!config) {
    console.warn('Messaging configuration not found, using endpoint as-is')
    return endpoint
  }

  const cleanEndpoint = endpoint.startsWith('/') ? endpoint.slice(1) : endpoint

  if (config.backendDomain) {
    const protocol = getProtocolFromDomain(config.backendDomain)
    const cleanDomain = getCleanDomain(config.backendDomain)
    return `${protocol}://${cleanDomain}/api/${cleanEndpoint}`
  }

  return `${config.basePath}/api/${cleanEndpoint}`
}

/**
 * Get the base path from configuration
 */
export function getBasePath(): string {
  const config = window.MessagingConfig
  return config?.basePath || '/'
}

/**
 * Get the API base URL
 */
export function getApiBaseUrl(): string {
  const config = window.MessagingConfig

  if (!config) {
    return '/api'
  }

  if (config.backendDomain) {
    const protocol = getProtocolFromDomain(config.backendDomain)
    const cleanDomain = getCleanDomain(config.backendDomain)
    return `${protocol}://${cleanDomain}/api`
  }

  return `${config.basePath}/api`
}

export function requiresAuthentication(): boolean {
  const config = window.MessagingConfig
  return config?.auth?.enabled || false
}

export type AuthMode = 'basic' | 'apikey' | 'host' | 'custom' | 'none'

export function getAuthMode(): AuthMode {
  const config = window.MessagingConfig
  if (!config?.auth) return 'none'
  return config.auth.mode
}

/**
 * Consume a fragment-delivered access token for Host authentication.
 * The fragment is removed before the token is returned so it does not remain in browser history.
 */
export function consumeHostAccessTokenFromFragment(
  authMode: AuthMode = getAuthMode(),
): string | null {
  const fragment = new URLSearchParams(window.location.hash.slice(1))

  if (!fragment.has('access_token')) {
    return null
  }

  const token = fragment.get('access_token')?.trim()
  window.history.replaceState(
    window.history.state,
    '',
    `${window.location.pathname}${window.location.search}`,
  )

  if (authMode !== 'host' || !token) {
    return null
  }

  return token.startsWith('Bearer ') ? token : `Bearer ${token}`
}

// Matches the server default of MessagingDashboardOptionsBuilder.SetStatsPollingInterval.
export const DEFAULT_STATS_POLLING_INTERVAL_MS = 2000

export function getStatsPollingInterval(): number {
  const interval = window.MessagingConfig?.statsPollingInterval
  return typeof interval === 'number' && Number.isFinite(interval) && interval > 0
    ? interval
    : DEFAULT_STATS_POLLING_INTERVAL_MS
}

function getProtocolFromDomain(domain: string): string {
  if (domain.startsWith('ssl:')) {
    return 'https'
  }
  return 'http'
}

function getCleanDomain(domain: string): string {
  if (domain.startsWith('ssl:')) {
    return domain.substring(4)
  }
  return domain
}

export function hasConfig(): boolean {
  return !!window.MessagingConfig
}
