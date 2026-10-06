import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import { authService } from '@/services/auth'
import { httpService } from '@/services/http'
import Login from '@/views/Login.vue'

const CREDENTIAL = 'Tenant acme;secret-value'

const createTestRouter = (): Router =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login', name: 'Login', component: { render: () => null } },
      { path: '/', name: 'Dashboard', component: { render: () => null } },
    ],
  })

const authorizationOf = (call: unknown[] | undefined) =>
  ((call?.[1] as RequestInit | undefined)?.headers as Record<string, string> | undefined)
    ?.Authorization

describe('custom authentication login', () => {
  let mounted: Mounted | undefined

  beforeEach(async () => {
    localStorage.clear()
    window.MessagingConfig = {
      basePath: '/messaging',
      auth: { mode: 'custom', enabled: true, sessionTimeout: 60 },
    }
    await authService.initialize()
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    localStorage.clear()
    delete window.MessagingConfig
  })

  it('validates a credential entered in the custom form and sends it verbatim as the Authorization header', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers({ 'content-type': 'application/json' }),
      json: async () => ({ authenticated: true, username: 'custom-user' }),
    })
    vi.stubGlobal('fetch', fetchMock)
    const router = createTestRouter()
    await router.push('/login')
    mounted = mountWithApp(Login, [router])
    await flush()

    expect(document.body.textContent).not.toContain('Public Dashboard')
    const input = document.querySelector<HTMLInputElement>('input[type="password"]')
    expect(input).not.toBeNull()
    expect(document.querySelector(`label[for="${input!.id}"]`)?.textContent).toContain('Credential')

    input!.value = CREDENTIAL
    input!.dispatchEvent(new Event('input'))
    await flush()
    document.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }))
    await flush()

    expect(fetchMock).toHaveBeenCalledWith(
      '/messaging/api/auth/validate',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(authorizationOf(fetchMock.mock.calls[0])).toBe(CREDENTIAL)
    expect(router.currentRoute.value.name).toBe('Dashboard')

    await httpService.get('/stats')
    expect(authorizationOf(fetchMock.mock.calls.at(-1))).toBe(CREDENTIAL)
  })
})
