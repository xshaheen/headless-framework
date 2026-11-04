import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import Login from '@/views/Login.vue'

const createTestRouter = (): Router =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login', name: 'Login', component: { render: () => null } },
      { path: '/', name: 'Dashboard', component: { render: () => null } },
      { path: '/scheduled', name: 'Scheduled', component: { render: () => null } },
    ],
  })

const infoResponse = (body: Record<string, unknown>) => ({
  ok: true,
  status: 200,
  headers: new Headers({ 'content-type': 'application/json' }),
  json: async () => body,
})

const hostInfo = (overrides: Record<string, unknown>) => ({
  mode: 'host',
  enabled: true,
  signInAvailable: true,
  authenticated: false,
  name: null,
  canOperate: false,
  ...overrides,
})

const signInLink = () =>
  [...document.querySelectorAll<HTMLAnchorElement>('a')].find((a) =>
    a.textContent?.includes('Sign in'),
  )

describe('host mode sign-in page', () => {
  let mounted: Mounted | undefined

  beforeEach(() => {
    localStorage.clear()
    window.MessagingConfig = {
      basePath: '/messaging/dashboard',
      auth: { mode: 'host', enabled: true, signInAvailable: true },
    }
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('links Sign in to the host sign-in endpoint with the requested page as the return path', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(infoResponse(hostInfo({}))))
    const router = createTestRouter()
    await router.push('/login?redirect=/scheduled')

    mounted = mountWithApp(Login, [router])
    await flush()

    expect(signInLink()?.getAttribute('href')).toBe(
      '/messaging/dashboard/auth/sign-in?returnUrl=%2Fmessaging%2Fdashboard%2Fscheduled',
    )
    expect(document.querySelector('input')).toBeNull()
    expect(router.currentRoute.value.name).toBe('Login')
  })

  it('tells the operator to open the dashboard from the application when no sign-in is configured', async () => {
    window.MessagingConfig!.auth.signInAvailable = false
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(infoResponse(hostInfo({ signInAvailable: false }))),
    )
    const router = createTestRouter()
    await router.push('/login')

    mounted = mountWithApp(Login, [router])
    await flush()

    expect(signInLink()).toBeUndefined()
    expect(document.body.textContent).toContain('Open the dashboard from your application')
  })

  it('shows the signed-out page and stays there without signing in again on its own', async () => {
    const fetchMock = vi.fn().mockResolvedValue(infoResponse(hostInfo({})))
    vi.stubGlobal('fetch', fetchMock)
    const router = createTestRouter()
    await router.push('/login?signedOut=1')

    mounted = mountWithApp(Login, [router])
    await flush()

    expect(document.body.textContent).toContain('You have signed out')
    expect(signInLink()?.getAttribute('href')).toBe(
      '/messaging/dashboard/auth/sign-in?returnUrl=%2Fmessaging%2Fdashboard%2F',
    )
    expect(router.currentRoute.value.name).toBe('Login')
    for (const [url] of fetchMock.mock.calls) {
      expect(String(url)).not.toContain('/auth/sign-in')
      expect(String(url)).not.toContain('/api/auth/session')
    }
  })

  it('moves a signed-in operator on to the requested page without storing a credential', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          infoResponse(hostInfo({ authenticated: true, name: 'ops@acme', canOperate: true })),
        ),
    )
    const router = createTestRouter()
    await router.push('/login?redirect=/scheduled')

    mounted = mountWithApp(Login, [router])
    await flush()

    expect(router.currentRoute.value.name).toBe('Scheduled')
    expect(localStorage.length).toBe(0)
  })
})
