/**
 * Utility functions for resolving paths using the Messaging configuration
 */

import type { AuthMode } from '@/services/auth'

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

/** The origin-relative root (or backend domain) the dashboard's API and auth endpoints live under. */
export function getBaseUrl(): string {
  const config = window.MessagingConfig
  return config?.backendDomain || config?.basePath || '/messaging/dashboard'
}

export function requiresAuthentication(): boolean {
  const config = window.MessagingConfig
  return config?.auth?.enabled || false
}

export type { AuthMode } from '@/services/auth'

export function getAuthMode(): AuthMode {
  const config = window.MessagingConfig
  if (!config?.auth) return 'none'
  return config.auth.mode
}

/** True in host mode, where the server serves every request locally and never relays to another node. */
export function isHostMode(): boolean {
  return getAuthMode() === 'host'
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
