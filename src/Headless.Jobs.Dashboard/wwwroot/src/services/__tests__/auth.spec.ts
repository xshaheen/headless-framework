import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const validated = (body: Record<string, unknown>) =>
  vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => body })

describe('Basic and API key sign-in', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const useMode = (mode: 'basic' | 'apikey') => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode, enabled: true, signInAvailable: false } }
  }

  it('stores a Basic credential, validates it with its header, and sends it on later calls', async () => {
    useMode('basic')
    const fetchMock = validated({ authenticated: true, username: 'admin' })
    vi.stubGlobal('fetch', fetchMock)
    const { authService, getAuthorizationHeader } = await import('@/services/auth')

    const loggedIn = await authService.login({ username: 'admin', password: 'pass' })

    const expectedHeader = `Basic ${btoa('admin:pass')}`
    expect(loggedIn).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith(
      '/jobs/dashboard/api/auth/validate',
      expect.objectContaining({ method: 'POST', headers: { Authorization: expectedHeader } }),
    )
    expect(localStorage.getItem('jobs_basic_auth')).toBe(btoa('admin:pass'))
    expect(getAuthorizationHeader()).toBe(expectedHeader)
    expect(authService.getStatus()).toMatchObject({ authenticated: true, username: 'admin', canOperate: true })
  })

  it('stores an API key and sends it as a bearer token', async () => {
    useMode('apikey')
    vi.stubGlobal('fetch', validated({ authenticated: true, username: 'api-user' }))
    const { authService, getAuthorizationHeader } = await import('@/services/auth')

    expect(await authService.login({ apiKey: 'secret-key-123' })).toBe(true)

    expect(localStorage.getItem('jobs_api_key')).toBe('secret-key-123')
    expect(getAuthorizationHeader()).toBe('Bearer secret-key-123')
  })

  it('clears a credential the server rejects', async () => {
    useMode('basic')
    vi.stubGlobal('fetch', validated({ authenticated: false, message: 'Invalid credentials' }))
    const { authService, getAuthorizationHeader } = await import('@/services/auth')

    const loggedIn = await authService.login({ username: 'admin', password: 'wrong' })

    expect(loggedIn).toBe(false)
    expect(authService.getStatus()).toMatchObject({ authenticated: false, message: 'Invalid credentials' })
    expect(localStorage.getItem('jobs_basic_auth')).toBeNull()
    expect(getAuthorizationHeader()).toBeNull()
  })

  it('fails closed when authentication configuration is unavailable', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const { authService } = await import('@/services/auth')

    const loggedIn = await authService.login({ username: 'admin', password: 'pass' })

    expect(loggedIn).toBe(false)
    expect(fetchMock).not.toHaveBeenCalled()
    expect(authService.getStatus()).toMatchObject({ authenticated: false })
    expect(localStorage.length).toBe(0)
  })
})

describe('host mode credentials', () => {
  afterEach(() => {
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('sends no Authorization header, even when an older credential is still stored', async () => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'host', enabled: true, signInAvailable: true } }
    localStorage.setItem('jobs_basic_auth', btoa('stale:value'))
    localStorage.setItem('jobs_host_access_key', 'Bearer stale-token')
    const { getAuthorizationHeader, getHubAccessToken } = await import('@/services/auth')

    expect(getAuthorizationHeader()).toBeNull()
    expect(getHubAccessToken()).toBeNull()
  })
})
