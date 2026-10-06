import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { SESSION_EXPIRED_MESSAGE } from '@/services/auth'
import { httpService } from '@/services/http'
import { useAuthStore } from '@/stores/authStore'
import router from '@/router'

const MINUTE = 60_000
const SIGNED_IN_AT = new Date('2026-01-01T00:00:00Z').getTime()

const signIn = () => {
  localStorage.setItem('messaging_api_key', ['fake', 'key'].join('-'))
  localStorage.setItem('messaging_signed_in_at', SIGNED_IN_AT.toString())
}

describe('session timeout across navigation and requests', () => {
  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(SIGNED_IN_AT)
    vi.spyOn(console, 'error').mockImplementation(() => {})
    localStorage.clear()
    setActivePinia(createPinia())
    window.MessagingConfig = {
      basePath: '/messaging',
      auth: { mode: 'apikey', enabled: true, sessionTimeout: 30 },
    }
    await router.replace('/login')
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('sends a stale sign-in to the login page with a visible message on app start', async () => {
    signIn()
    vi.setSystemTime(SIGNED_IN_AT + 31 * MINUTE)

    await router.push('/scheduled')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(router.currentRoute.value.query.redirect).toBe('/scheduled')
    expect(useAuthStore().errorMessage).toBe(SESSION_EXPIRED_MESSAGE)
    expect(localStorage.getItem('messaging_api_key')).toBeNull()
  })

  it('expires a signed-in session on a later navigation', async () => {
    signIn()
    await router.push('/scheduled')
    expect(router.currentRoute.value.name).toBe('Scheduled')

    vi.setSystemTime(SIGNED_IN_AT + 30 * MINUTE)
    await router.push('/subscribers')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(useAuthStore().errorMessage).toBe(SESSION_EXPIRED_MESSAGE)
  })

  it('sends the user to sign in when an API request finds the session expired', async () => {
    signIn()
    await router.push('/scheduled')
    vi.stubGlobal('fetch', vi.fn())

    vi.setSystemTime(SIGNED_IN_AT + 45 * MINUTE)
    await expect(httpService.get('/stats')).rejects.toThrow('Session expired')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('Login'))

    expect(router.currentRoute.value.query.redirect).toBe('/scheduled')
    expect(useAuthStore().errorMessage).toBe(SESSION_EXPIRED_MESSAGE)
    expect(fetch).not.toHaveBeenCalled()
  })
})
