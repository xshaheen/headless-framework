import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { httpService } from '@/services/http'
import router from '@/router'

const info = (authenticated: boolean) => ({
  ok: true,
  status: 200,
  headers: new Headers({ 'content-type': 'application/json' }),
  json: async () => ({
    mode: 'host',
    enabled: true,
    signInAvailable: true,
    authenticated,
    name: authenticated ? 'ops@acme' : null,
    canOperate: authenticated,
  }),
})

const unauthorized = { ok: false, status: 401, headers: new Headers(), text: async () => '' }

describe('router in host mode', () => {
  beforeEach(async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    localStorage.clear()
    setActivePinia(createPinia())
    window.MessagingConfig = {
      basePath: '/messaging/dashboard',
      auth: { mode: 'host', enabled: true, signInAvailable: true },
    }
    await router.replace('/login')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('asks auth/info before the first page and lets a signed-in operator in without storing a credential', async () => {
    const fetchMock = vi.fn().mockResolvedValue(info(true))
    vi.stubGlobal('fetch', fetchMock)

    await router.push('/scheduled')

    expect(fetchMock.mock.calls[0]![0]).toBe('/messaging/dashboard/api/auth/info')
    expect(router.currentRoute.value.name).toBe('Scheduled')
    expect(localStorage.length).toBe(0)
  })

  it('sends an operator without a dashboard session to the sign-in page with the requested path', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(info(false)))

    await router.push('/scheduled')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(router.currentRoute.value.query.redirect).toBe('/scheduled')
  })

  it('returns to the sign-in page with the current path when the API answers 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(info(true)).mockResolvedValue(unauthorized))
    await router.push('/published')

    await expect(httpService.get('/stats')).rejects.toThrow('Authentication required')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('Login'))

    expect(router.currentRoute.value.query.redirect).toBe('/published')
  })
})
