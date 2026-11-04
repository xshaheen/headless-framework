import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authService } from '@/services/auth'
import { httpService } from '@/services/http'

const jsonResponse = (body: unknown, status = 200) => ({
  ok: status >= 200 && status < 300,
  status,
  headers: new Headers({ 'content-type': 'application/json' }),
  json: async () => body,
  text: async () => JSON.stringify(body),
})

// Assembled at run time so secret scanners do not read a test fixture as a leaked key.
const TEST_API_KEY = ['test', 'api', 'key', '0001'].join('-')

const headersOf = (call: unknown[] | undefined) =>
  ((call?.[1] as RequestInit | undefined)?.headers ?? {}) as Record<string, string>

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  localStorage.clear()
  delete window.MessagingConfig
})

describe('host mode session', () => {
  beforeEach(() => {
    localStorage.clear()
    window.MessagingConfig = {
      basePath: '/messaging/dashboard',
      auth: { mode: 'host', enabled: true, signInAvailable: true },
    }
  })

  it('reads the signed-in operator and the operate permission from auth/info', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        mode: 'host',
        enabled: true,
        signInAvailable: true,
        authenticated: true,
        name: 'ops@acme',
        canOperate: false,
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await authService.initialize()

    expect(fetchMock).toHaveBeenCalledWith(
      '/messaging/dashboard/api/auth/info',
      expect.objectContaining({ method: 'GET', credentials: 'same-origin' }),
    )
    expect(authService.getStatus()).toEqual({ authenticated: true, username: 'ops@acme', canOperate: false })
    expect(authService.getAuthHeader()).toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('treats an unreadable auth/info answer as signed out', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ unexpected: true })))

    await authService.initialize()

    expect(authService.isAuthenticated()).toBe(false)
    expect(authService.getStatus().canOperate).toBe(false)
  })

  it('signs out by posting to the sign-out endpoint with the script-request header', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({ authenticated: true, name: 'ops@acme', canOperate: true, mode: 'host' }),
      )
      .mockResolvedValueOnce({ ok: true, status: 204, headers: new Headers() })
    vi.stubGlobal('fetch', fetchMock)
    await authService.initialize()

    await authService.signOut()

    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(url).toBe('/messaging/dashboard/api/auth/sign-out')
    expect(init.method).toBe('POST')
    expect(headersOf(fetchMock.mock.calls[1])['X-Requested-With']).toBe('XMLHttpRequest')
    expect(authService.isAuthenticated()).toBe(false)
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('builds the sign-in URL with the return path under the dashboard base', () => {
    expect(authService.getSignInUrl('/published?tab=failed')).toBe(
      '/messaging/dashboard/auth/sign-in?returnUrl=%2Fmessaging%2Fdashboard%2Fpublished%3Ftab%3Dfailed',
    )
  })
})

describe('Basic and API key sign-in', () => {
  it('stores a Basic credential, sends it on later requests, and lets the user operate', async () => {
    window.MessagingConfig = { basePath: '/messaging/dashboard', auth: { mode: 'basic', enabled: true } }
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ authenticated: true, username: 'ops' }))
    vi.stubGlobal('fetch', fetchMock)
    await authService.initialize()

    const loggedIn = await authService.login({ username: 'ops', password: 'pa55' })

    expect(loggedIn).toBe(true)
    expect(fetchMock.mock.calls[0]![0]).toBe('/messaging/dashboard/api/auth/validate')
    expect(headersOf(fetchMock.mock.calls[0]).Authorization).toBe(`Basic ${btoa('ops:pa55')}`)
    expect(headersOf(fetchMock.mock.calls[0])['X-Requested-With']).toBe('XMLHttpRequest')
    expect(localStorage.getItem('messaging_basic_auth')).toBe(btoa('ops:pa55'))
    expect(authService.getStatus().canOperate).toBe(true)

    await httpService.get('/stats')
    expect(headersOf(fetchMock.mock.calls.at(-1)).Authorization).toBe(`Basic ${btoa('ops:pa55')}`)
  })

  it('stores an API key and sends it as a bearer credential', async () => {
    window.MessagingConfig = { basePath: '/messaging/dashboard', auth: { mode: 'apikey', enabled: true } }
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ authenticated: true }))
    vi.stubGlobal('fetch', fetchMock)
    await authService.initialize()

    await authService.login({ apiKey: TEST_API_KEY })
    await httpService.post('/published/requeue', ['a'])

    expect(localStorage.getItem('messaging_api_key')).toBe(TEST_API_KEY)
    expect(headersOf(fetchMock.mock.calls.at(-1)).Authorization).toBe(`Bearer ${TEST_API_KEY}`)
  })

  it('clears a credential the server refuses', async () => {
    window.MessagingConfig = { basePath: '/messaging/dashboard', auth: { mode: 'apikey', enabled: true } }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(null, 401)))
    await authService.initialize()

    const loggedIn = await authService.login({ apiKey: TEST_API_KEY })

    expect(loggedIn).toBe(false)
    expect(localStorage.getItem('messaging_api_key')).toBeNull()
  })
})
