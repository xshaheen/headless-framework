import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/authStore'
import router from '@/router'

const signedIn = {
  ok: true,
  status: 200,
  headers: new Headers({ 'content-type': 'application/json' }),
  json: async () => ({
    mode: 'host',
    enabled: true,
    signInAvailable: true,
    authenticated: true,
    name: 'ops@acme',
    canOperate: true,
  }),
}

describe('router when parallel requests all answer 401', () => {
  beforeEach(async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    localStorage.clear()
    setActivePinia(createPinia())
    window.MessagingConfig = {
      basePath: '/messaging/dashboard',
      auth: { mode: 'host', enabled: true, signInAvailable: true },
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(signedIn))
    await router.replace('/login')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('keeps the session-ended message and redirects to the sign-in page once', async () => {
    await router.push('/published')
    const push = vi.spyOn(router, 'push')

    window.dispatchEvent(new CustomEvent('auth:logout'))
    window.dispatchEvent(new CustomEvent('auth:logout'))
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('Login'))

    expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'Login', query: { redirect: '/published' } })
    expect(useAuthStore().errorMessage).toBe('Your session ended. Please sign in again.')
  })
})
