import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

// Route components are irrelevant to the guard; the real views open SignalR connections on import.
vi.mock('@/views/TimeJob.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/Dashboard.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/Login.vue', () => ({ default: { render: () => null } }))

const INFO_URL = '/jobs/dashboard/api/auth/info'

const infoResponse = (body: Record<string, unknown>) =>
  vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => body })

describe('router guard in host mode', () => {
  beforeEach(() => {
    localStorage.clear()
    window.JobsConfig = {
      basePath: '/jobs/dashboard',
      auth: { mode: 'host', enabled: true, signInAvailable: true },
    }
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const load = async () => ({
    router: (await import('@/router')).default,
    authStore: (await import('@/stores/authStore')).useAuthStore(),
  })

  it('asks the server for the session before the first route and lets a signed-in operator through', async () => {
    const fetchMock = infoResponse({
      mode: 'host',
      enabled: true,
      signInAvailable: true,
      authenticated: true,
      name: 'ops@example.test',
      canOperate: false,
    })
    vi.stubGlobal('fetch', fetchMock)
    const { router, authStore } = await load()

    await router.push('/time-jobs')

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0]![0]).toBe(INFO_URL)
    expect(router.currentRoute.value.name).toBe('TimeJob')
    expect(authStore.username).toBe('ops@example.test')
    expect(authStore.canOperate).toBe(false)
    expect(localStorage.length).toBe(0)
  })

  it('sends a visitor without a dashboard session to the login page with the path to return to', async () => {
    vi.stubGlobal(
      'fetch',
      infoResponse({
        mode: 'host',
        enabled: true,
        signInAvailable: true,
        authenticated: false,
        name: null,
        canOperate: false,
      }),
    )
    const { router, authStore } = await load()

    await router.push('/time-jobs?page=2')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(router.currentRoute.value.query.redirect).toBe('/time-jobs?page=2')
    expect(authStore.isLoggedIn).toBe(false)
  })

  it('treats a failed session lookup as signed out', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network down')))
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const { router } = await load()

    await router.push('/')

    expect(router.currentRoute.value.name).toBe('Login')
  })
})
