import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'

const { push, query } = vi.hoisted(() => ({ push: vi.fn(), query: { value: {} as Record<string, string> } }))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push, currentRoute: { value: { query: query.value } } }),
}))

const signedOutInfo = { mode: 'host', enabled: true, authenticated: false, name: null, canOperate: false }

describe('Login in host mode', () => {
  let mounted: Mounted | undefined

  beforeEach(() => {
    localStorage.clear()
    push.mockReset()
    query.value = {}
    window.history.replaceState(null, '', '/jobs/dashboard/login')
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const mountLogin = async (signInAvailable: boolean) => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'host', enabled: true, signInAvailable } }
    const Login = (await import('@/views/Login.vue')).default
    mounted = mountWithApp(Login)
    await flush()
  }

  const signInLink = () =>
    Array.from(document.querySelectorAll<HTMLAnchorElement>('a')).find((a) => a.textContent?.includes('Sign in'))

  it('offers a Sign in link that signs in through the host and returns to the requested page', async () => {
    query.value = { redirect: '/time-jobs?page=2' }
    await mountLogin(true)

    expect(signInLink()?.getAttribute('href')).toBe(
      '/jobs/dashboard/auth/sign-in?returnUrl=%2Fjobs%2Fdashboard%2Ftime-jobs%3Fpage%3D2',
    )
    expect(document.querySelectorAll('input')).toHaveLength(0)
  })

  it('returns to the dashboard root when no page was requested', async () => {
    await mountLogin(true)

    expect(signInLink()?.getAttribute('href')).toBe('/jobs/dashboard/auth/sign-in?returnUrl=%2Fjobs%2Fdashboard%2F')
  })

  it('tells the operator to open the dashboard from the application when the host offers no sign-in', async () => {
    await mountLogin(false)

    expect(signInLink()).toBeUndefined()
    expect(document.body.textContent).toContain('Open the dashboard from your application')
    expect(document.querySelectorAll('input')).toHaveLength(0)
  })

  it('after sign-out shows the signed-out page and does not start a sign-in on its own', async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url.endsWith('/api/auth/sign-out')
        ? { ok: true, status: 204, json: async () => ({}) }
        : { ok: true, status: 200, json: async () => ({ ...signedOutInfo, authenticated: true, name: 'ops' }) },
    )
    vi.stubGlobal('fetch', fetchMock)
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'host', enabled: true, signInAvailable: true } }
    const { useAuthStore } = await import('@/stores/authStore')
    const Login = (await import('@/views/Login.vue')).default
    mounted = mountWithApp(Login)
    // Installing the app's Pinia makes it the active one, so this is the store the mounted Login reads.
    const authStore = useAuthStore()
    await authStore.initializeAuth()

    await authStore.logout()
    await flush()

    const signOutCall = fetchMock.mock.calls.find(([url]) => url === '/jobs/dashboard/api/auth/sign-out')
    expect(signOutCall).toBeDefined()
    expect(signOutCall![1]).toMatchObject({ method: 'POST', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
    expect(document.body.textContent).toContain('You have signed out')
    expect(signInLink()).toBeDefined()
    expect(push).not.toHaveBeenCalled()
    expect(window.location.pathname).toBe('/jobs/dashboard/login')
  })
})
