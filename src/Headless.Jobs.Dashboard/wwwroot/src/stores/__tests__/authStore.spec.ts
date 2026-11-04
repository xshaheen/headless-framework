import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const { push, currentRoute } = vi.hoisted(() => ({
  push: vi.fn(),
  currentRoute: { value: { name: 'TimeJob', fullPath: '/time-jobs' } as { name: string; fullPath: string } },
}))

vi.mock('@/router', () => ({ default: { push, currentRoute } }))

describe('auth store', () => {
  beforeEach(() => {
    localStorage.clear()
    currentRoute.value = { name: 'TimeJob', fullPath: '/time-jobs' }
    // Like the real router, the route changes only once the navigation completes.
    push.mockReset().mockImplementation(async () => {
      await Promise.resolve()
      currentRoute.value = { name: 'Login', fullPath: '/login' }
    })
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'host', enabled: true, signInAvailable: true } }
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('keeps the session-ended message and redirects once when parallel requests all answer 401', async () => {
    const { useAuthStore } = await import('@/stores/authStore')
    const authStore = useAuthStore()
    authStore.authStatus = { authenticated: true, username: 'operator', canOperate: true }

    await Promise.all([authStore.handleUnauthorized(), authStore.handleUnauthorized()])

    expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'Login', query: { redirect: '/time-jobs' } })
    expect(authStore.authStatus.authenticated).toBe(false)
    expect(authStore.errorMessage).toBe('Your session has ended. Please sign in again.')
  })
})
