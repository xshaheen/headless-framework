import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

describe('host authentication login', () => {
  beforeEach(() => {
    localStorage.clear()
    window.JobsConfig = {
      basePath: '/jobs/dashboard',
      auth: { mode: 'host', enabled: true, sessionTimeout: 60 },
    }
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('initializes authentication before validating a fragment-delivered access token', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        authenticated: true,
        username: 'host-user',
        message: 'Authentication successful',
      }),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { authService } = await import('@/services/auth')

    const loggedIn = await authService.login({ hostAccessKey: 'Bearer demo.jwt' })

    expect(loggedIn).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith(
      '/jobs/dashboard/api/auth/validate',
      expect.objectContaining({
        method: 'POST',
        headers: { Authorization: 'Bearer demo.jwt' },
        signal: expect.any(AbortSignal),
      }),
    )
    expect(authService.getStatus()).toMatchObject({ authenticated: true, username: 'host-user' })
    expect(localStorage.getItem('jobs_host_access_key')).toBe('Bearer demo.jwt')
  })

  it('clears a fragment-delivered token when validation rejects it', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ authenticated: false, message: 'Invalid access token' }),
      }),
    )
    const { authService } = await import('@/services/auth')

    const loggedIn = await authService.login({ hostAccessKey: 'Bearer rejected.jwt' })

    expect(loggedIn).toBe(false)
    expect(authService.getStatus()).toMatchObject({
      authenticated: false,
      message: 'Invalid access token',
    })
    expect(localStorage.getItem('jobs_host_access_key')).toBeNull()
  })

  it('fails closed when authentication configuration is unavailable', async () => {
    delete window.JobsConfig
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const { authService } = await import('@/services/auth')

    const loggedIn = await authService.login({ hostAccessKey: 'Bearer demo.jwt' })

    expect(loggedIn).toBe(false)
    expect(fetchMock).not.toHaveBeenCalled()
    expect(authService.getStatus()).toMatchObject({ authenticated: false })
    expect(localStorage.getItem('jobs_host_access_key')).toBeNull()
  })
})
