/**
 * Clean, simple HTTP service for Messaging Dashboard
 */

import { authService } from './auth'

/**
 * Thrown for any non-2xx, non-401 response. Carries the parsed JSON body (or raw text) so
 * callers can render outcome-specific messages (403 remedy, 404 not-found, 409 conflict) instead
 * of a generic failure — the session stays signed in for all of these.
 */
export class HttpError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, body: unknown) {
    super(`HTTP ${status}`)
    this.name = 'HttpError'
    this.status = status
    this.body = body
  }
}

class HttpService {
  private baseUrl: string

  constructor() {
    const config = window.MessagingConfig
    this.baseUrl = config?.backendDomain || config?.basePath || '/messaging'
  }

  /**
   * GET request
   */
  async get<T>(endpoint: string): Promise<T> {
    return this.request<T>('GET', endpoint)
  }

  /**
   * POST request
   */
  async post<T>(endpoint: string, data?: unknown): Promise<T> {
    return this.request<T>('POST', endpoint, data)
  }

  /**
   * PUT request
   */
  async put<T>(endpoint: string, data?: unknown): Promise<T> {
    return this.request<T>('PUT', endpoint, data)
  }

  /**
   * DELETE request
   */
  async delete<T>(endpoint: string): Promise<T> {
    return this.request<T>('DELETE', endpoint)
  }

  /**
   * Generic request method
   */
  private async request<T>(method: string, endpoint: string, data?: unknown): Promise<T> {
    const url = `${this.baseUrl}/api${endpoint}`

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
    }

    // Add authentication header if available
    const authHeader = authService.getAuthHeader()
    if (authHeader) {
      headers['Authorization'] = authHeader
    }

    const config: RequestInit = {
      method,
      headers,
      body: data ? JSON.stringify(data) : undefined,
    }

    try {
      const response = await fetch(url, config)

      // Only an unauthenticated (401) response ends the session; an authenticated principal
      // without a usable actor (403), a stale fence (404/409), or a JSON validation failure (422)
      // all carry a parsed body the caller renders in place, never a logout.
      if (response.status === 401) {
        console.warn('Authentication failed, logging out')
        authService.logout()
        window.dispatchEvent(new CustomEvent('auth:logout'))
        throw new Error('Authentication required')
      }

      if (!response.ok) {
        throw new HttpError(response.status, await parseResponseBody(response))
      }

      // Handle empty responses
      const contentType = response.headers.get('content-type')
      if (contentType && contentType.includes('application/json')) {
        return response.json()
      }

      return response.text() as unknown as T
    } catch (error) {
      console.error(`HTTP ${method} ${endpoint} failed:`, error)
      throw error
    }
  }
}

async function parseResponseBody(response: Response): Promise<unknown> {
  const text = await response.text()
  if (!text) return null

  const contentType = response.headers.get('content-type')
  if (contentType && contentType.includes('application/json')) {
    try {
      return JSON.parse(text)
    } catch {
      return text
    }
  }

  return text
}

// Export singleton instance
export const httpService = new HttpService()
