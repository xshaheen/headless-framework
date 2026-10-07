import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const SIGNED_IN = new Date('2026-05-01T10:00:00Z').getTime()
const MINUTE = 60_000

// Route components are irrelevant to the guard; the real views open SignalR connections on import.
vi.mock('@/views/TimeJob.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/Login.vue', () => ({ default: { render: () => null } }))

describe('router session guard', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now: SIGNED_IN, toFake: ['Date'] })
    localStorage.clear()
    window.JobsConfig = {
      basePath: '/',
      auth: { mode: 'basic', enabled: true, sessionTimeout: 30 },
    }
    localStorage.setItem('jobs_basic_auth', btoa('tester:fake'))
    localStorage.setItem('jobs_signed_in_at', String(SIGNED_IN))
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const load = async () => ({
    router: (await import('@/router')).default,
    authStore: (await import('@/stores/authStore')).useAuthStore(),
  })

  it('lets a fresh session through on app start', async () => {
    const { router } = await load()

    await router.push('/time-jobs')

    expect(router.currentRoute.value.name).toBe('TimeJob')
  })

  it('sends a stale session to the login page with the expiry message on app start', async () => {
    vi.setSystemTime(SIGNED_IN + 30 * MINUTE)
    const { router, authStore } = await load()

    await router.push('/time-jobs')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(router.currentRoute.value.query.redirect).toBe('/time-jobs')
    expect(authStore.errorMessage).toBe('Session expired. Please log in again.')
    expect(localStorage.getItem('jobs_basic_auth')).toBeNull()
  })

  it('expires the session on a later navigation once the timeout elapses', async () => {
    const { router, authStore } = await load()
    await router.push('/time-jobs')

    vi.setSystemTime(SIGNED_IN + 30 * MINUTE)
    await router.push('/time-jobs?page=2')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(authStore.isLoggedIn).toBe(false)
  })
})
