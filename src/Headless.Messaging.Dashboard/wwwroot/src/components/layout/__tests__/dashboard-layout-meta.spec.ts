import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import { httpService } from '@/services/http'
import DashboardLayout from '@/components/layout/DashboardLayout.vue'

const Page = { render: () => null }

const createTestRouter = (): Router =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'Dashboard', component: Page },
      { path: '/scheduled', name: 'Scheduled', component: Page },
      { path: '/login', name: 'Login', component: Page },
    ],
  })

const capabilitiesButton = () =>
  document.querySelector<HTMLButtonElement>('.provider-capabilities-trigger')

describe('provider capabilities outside the overview', () => {
  let mounted: Mounted | undefined

  beforeEach(() => {
    window.MessagingConfig = {
      basePath: '/messaging',
      auth: { mode: 'none', enabled: false, sessionTimeout: 0 },
    }
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    delete window.MessagingConfig
  })

  it('loads provider metadata once when the layout mounts on a deep-linked page', async () => {
    const get = vi.spyOn(httpService, 'get').mockImplementation(async (endpoint: string) => {
      if (endpoint === '/meta') {
        return {
          providerCapabilities: [
            { provider: 'RabbitMQ', role: 'Transport', lanes: ['Bus'] },
            { provider: 'PostgreSQL', role: 'Storage' },
          ],
        }
      }
      throw new Error(`Unexpected request ${endpoint}`)
    })
    const router = createTestRouter()
    await router.push('/scheduled')

    mounted = mountWithApp(DashboardLayout, [router])
    await flush()

    expect(get.mock.calls.filter(([endpoint]) => endpoint === '/meta')).toHaveLength(1)
    expect(capabilitiesButton()?.getAttribute('aria-label')).toBe('Show provider capabilities (2)')
  })

  it('sends no metadata request when the first navigation lands on the login page', async () => {
    const get = vi.spyOn(httpService, 'get').mockResolvedValue({})
    const router = createTestRouter()
    await router.push('/login')

    mounted = mountWithApp(DashboardLayout, [router])
    await flush()

    expect(get).not.toHaveBeenCalled()
  })
})
