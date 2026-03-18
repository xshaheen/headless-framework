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

export function getAuthMode(): 'basic' | 'apikey' | 'host' | 'none' {
  const config = window.MessagingConfig
  if (!config?.auth) return 'none'
  return config.auth.mode as 'basic' | 'apikey' | 'host' | 'none'
}

export function getStatsPollingInterval(): number {
  const config = window.MessagingConfig
  return config?.statsPollingInterval || 5000
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
