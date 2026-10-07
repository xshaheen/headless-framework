import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'

const CREDENTIAL = 'Token team=ops; sig=abc123'

const { push } = vi.hoisted(() => ({ push: vi.fn() }))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push, currentRoute: { value: { query: {} } } }),
}))

describe('Login', () => {
  let mounted: Mounted | undefined

  beforeEach(() => {
    localStorage.clear()
    push.mockReset()
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const mountLogin = async (mode: 'custom' | 'basic') => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode, enabled: true, sessionTimeout: 60 } }
    const Login = (await import('@/views/Login.vue')).default
    mounted = mountWithApp(Login)
    await flush()
  }

  it('offers custom mode a single password-type credential field and sends it verbatim', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ authenticated: true, username: 'custom-user' }),
    })
    vi.stubGlobal('fetch', fetchMock)
    await mountLogin('custom')

    const form = document.querySelector<HTMLFormElement>('.custom-auth-form')!
    const inputs = form.querySelectorAll<HTMLInputElement>('input')
    expect(inputs).toHaveLength(1)
    expect(inputs[0]!.type).toBe('password')
    expect(form.querySelector('label')?.textContent).toContain('Credential')
    expect(form.querySelector('button[type="submit"]')?.textContent).toContain('Authenticate')

    inputs[0]!.value = CREDENTIAL
    inputs[0]!.dispatchEvent(new Event('input'))
    await flush()
    form.dispatchEvent(new Event('submit', { cancelable: true }))
    await flush()

    expect(fetchMock).toHaveBeenCalledWith(
      '/jobs/dashboard/api/auth/validate',
      expect.objectContaining({ method: 'POST', headers: { Authorization: CREDENTIAL } }),
    )
    expect(localStorage.getItem('jobs_custom_credential')).toBe(CREDENTIAL)
    expect(push).toHaveBeenCalledWith('/')
  })

  it('shows why the user landed on the login page after the session expired', async () => {
    localStorage.setItem('jobs_basic_auth', btoa('tester:fake'))
    localStorage.setItem('jobs_signed_in_at', String(Date.now() - 61 * 60_000))
    await mountLogin('basic')

    const { useAuthStore } = await import('@/stores/authStore')
    expect(useAuthStore().enforceSessionTimeout()).toBe(true)
    await flush()

    expect(document.querySelector('.v-alert')?.textContent).toContain('Session expired. Please log in again.')
  })
})
