import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthConfig } from '@/services/auth'

const SIGNED_IN = new Date('2026-05-01T10:00:00Z').getTime()
const MINUTE = 60_000

const configure = (auth: Partial<AuthConfig>) => {
  window.JobsConfig = {
    basePath: '/jobs/dashboard',
    auth: { mode: 'basic', enabled: true, sessionTimeout: 60, ...auth },
  }
}

const validResponse = () =>
  vi.fn().mockResolvedValue({ ok: true, json: async () => ({ authenticated: true, username: 'admin' }) })

describe('session timeout', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now: SIGNED_IN, toFake: ['Date'] })
    localStorage.clear()
    configure({})
    vi.stubGlobal('fetch', validResponse())
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('records the sign-in time when credentials are stored and expires them once the timeout elapses', async () => {
    const { authService, isSessionExpired } = await import('@/services/auth')

    expect(await authService.login({ username: 'admin', password: 'secret' })).toBe(true)
    expect(localStorage.getItem('jobs_signed_in_at')).toBe(String(SIGNED_IN))

    vi.setSystemTime(SIGNED_IN + 60 * MINUTE - 1)
    expect(isSessionExpired()).toBe(false)
    expect(authService.expireStaleSession()).toBe(false)
    expect(localStorage.getItem('jobs_basic_auth')).not.toBeNull()

    vi.setSystemTime(SIGNED_IN + 60 * MINUTE)
    expect(isSessionExpired()).toBe(true)
    expect(authService.expireStaleSession()).toBe(true)
    expect(localStorage.getItem('jobs_basic_auth')).toBeNull()
    expect(localStorage.getItem('jobs_signed_in_at')).toBeNull()
    expect(authService.getStatus()).toEqual({
      authenticated: false,
      message: 'Session expired. Please log in again.',
    })
  })

  it('removes the sign-in time on logout', async () => {
    const { authService } = await import('@/services/auth')
    await authService.login({ username: 'admin', password: 'secret' })

    authService.logout()

    expect(localStorage.getItem('jobs_signed_in_at')).toBeNull()
  })

  it('treats stored credentials without a sign-in time as expired', async () => {
    localStorage.setItem('jobs_api_key', 'an-old-api-key')
    configure({ mode: 'apikey' })
    const { isSessionExpired } = await import('@/services/auth')

    expect(isSessionExpired()).toBe(true)
  })

  it.each([
    ['mode none', { mode: 'none' as const }],
    ['auth disabled', { enabled: false }],
    ['a zero timeout', { sessionTimeout: 0 }],
    ['a negative timeout', { sessionTimeout: -5 }],
    ['a non-finite timeout', { sessionTimeout: Number.NaN }],
  ])('never expires with %s', async (_, auth) => {
    localStorage.setItem('jobs_basic_auth', btoa('tester:fake'))
    localStorage.setItem('jobs_signed_in_at', String(SIGNED_IN))
    configure(auth)
    const { isSessionExpired } = await import('@/services/auth')

    vi.setSystemTime(SIGNED_IN + 365 * 24 * 60 * MINUTE)

    expect(isSessionExpired()).toBe(false)
  })

  it('has nothing to expire when no credential is stored', async () => {
    const { isSessionExpired } = await import('@/services/auth')

    expect(isSessionExpired()).toBe(false)
  })
})
