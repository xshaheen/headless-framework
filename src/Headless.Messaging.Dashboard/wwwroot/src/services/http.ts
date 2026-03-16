/**
 * Clean, simple HTTP service for Messaging Dashboard
 */

import { authService } from './auth'

class HttpService {
  private baseUrl: string

  constructor() {
    const config = window.MessagingConfig
    this.baseUrl = config?.backendDomain || config?.basePath || '/messaging/dashboard'
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

      // Handle authentication errors
      if (response.status === 401) {
        console.warn('Authentication failed, logging out')
        authService.logout()
        window.dispatchEvent(new CustomEvent('auth:logout'))
        throw new Error('Authentication required')
      }

      if (!response.ok) {
        const errorText = await response.text()
        throw new Error(`HTTP ${response.status}: ${errorText}`)
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

// Export singleton instance
export const httpService = new HttpService()

// Export types for convenience
export interface ApiResponse<T> {
  data: T
  message?: string
  timestamp?: string
}

export interface PaginatedResponse<T> {
  data: T[]
  total: number
  page: number
  pageSize: number
}

export interface ErrorResponse {
  error: string
  message: string
  timestamp: string
}
