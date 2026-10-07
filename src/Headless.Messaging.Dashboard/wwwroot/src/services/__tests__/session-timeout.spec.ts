import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { authService, SESSION_EXPIRED_EVENT, SESSION_EXPIRED_MESSAGE } from '@/services/auth'
import { httpService } from '@/services/http'

type AuthMode = 'none' | 'basic' | 'apikey' | 'host' | 'custom'

const MINUTE = 60_000

const configure = (mode: AuthMode, sessionTimeout: number, enabled = mode !== 'none') => {
  window.MessagingConfig = { basePath: '/messaging', auth: { mode, enabled, sessionTimeout } }
}

const validateOk = () =>
  vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => ({ authenticated: true, username: 'admin' }),
  })

describe('session timeout', () => {
  beforeEach(async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'))
    vi.spyOn(console, 'error').mockImplementation(() => {})
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('keeps sending credentials until the configured timeout, then clears them and signals expiry', async () => {
    configure('apikey', 60)
    await authService.initialize()
    const fetchMock = validateOk()
    vi.stubGlobal('fetch', fetchMock)
    expect(await authService.login({ apiKey: ['fake', 'key'].join('-') })).toBe(true)
    const expired = vi.fn()
    window.addEventListener(SESSION_EXPIRED_EVENT, expired)

    vi.advanceTimersByTime(59 * MINUTE)
    await httpService.get('/stats')
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/messaging/api/stats',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: `Bearer ${['fake', 'key'].join('-')}` }),
      }),
    )

    vi.advanceTimersByTime(MINUTE)
    const callsBefore = fetchMock.mock.calls.length
    await expect(httpService.get('/stats')).rejects.toThrow('Session expired')

    expect(fetchMock).toHaveBeenCalledTimes(callsBefore)
    expect(expired).toHaveBeenCalledOnce()
    expect(localStorage.getItem('messaging_api_key')).toBeNull()
    expect(authService.getStatus()).toEqual({
      authenticated: false,
      message: SESSION_EXPIRED_MESSAGE,
    })
    window.removeEventListener(SESSION_EXPIRED_EVENT, expired)
  })

  it.each([
    ['a zero timeout', 'basic' as const, 0],
    ['a negative timeout', 'basic' as const, -5],
  ])('never expires with %s', async (_name, mode, timeout) => {
    configure(mode, timeout)
    await authService.initialize()
    vi.stubGlobal('fetch', validateOk())
    expect(await authService.login({ username: 'admin', password: 'secret' })).toBe(true)

    vi.advanceTimersByTime(365 * 24 * 60 * MINUTE)

    expect(authService.expireSessionIfStale()).toBe(false)
    expect(localStorage.getItem('messaging_basic_auth')).not.toBeNull()
  })

  it('never expires in mode none', async () => {
    configure('none', 1, false)
    localStorage.setItem('messaging_api_key', 'leftover')
    localStorage.setItem('messaging_signed_in_at', '1')

    expect(authService.expireSessionIfStale()).toBe(false)
    expect(localStorage.getItem('messaging_api_key')).toBe('leftover')
  })

  it('treats stored credentials without a sign-in time as expired', () => {
    configure('apikey', 60)
    localStorage.setItem('messaging_api_key', ['fake', 'key'].join('-'))

    expect(authService.expireSessionIfStale()).toBe(true)
    expect(localStorage.getItem('messaging_api_key')).toBeNull()
  })
})
