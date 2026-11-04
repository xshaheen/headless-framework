import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

// Route components are irrelevant to the guard; the real views open SignalR connections on import.
vi.mock('@/views/TimeJob.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/Login.vue', () => ({ default: { render: () => null } }))

describe('router guard in Basic mode', () => {
  beforeEach(() => {
    localStorage.clear()
    window.JobsConfig = {
      basePath: '/',
      auth: { mode: 'basic', enabled: true, signInAvailable: false },
    }
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const load = async () => (await import('@/router')).default

  it('lets a stored credential through without asking the server', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    localStorage.setItem('jobs_basic_auth', btoa('tester:fake'))
    const router = await load()

    await router.push('/time-jobs')

    expect(router.currentRoute.value.name).toBe('TimeJob')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('sends a visitor without a credential to the login page with the path to return to', async () => {
    const router = await load()

    await router.push('/time-jobs')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(router.currentRoute.value.query.redirect).toBe('/time-jobs')
  })

  it("ignores another mode's leftover credential", async () => {
    localStorage.setItem('jobs_api_key', 'leftover-api-key')
    const router = await load()

    await router.push('/time-jobs')

    expect(router.currentRoute.value.name).toBe('Login')
  })
})
