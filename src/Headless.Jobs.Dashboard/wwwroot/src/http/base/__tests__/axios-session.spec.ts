import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosAdapter, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'

const { push, currentRoute } = vi.hoisted(() => ({
  push: vi.fn(),
  currentRoute: { value: { name: 'TimeJob', fullPath: '/time-jobs?page=2' } },
}))

vi.mock('@/router', () => ({ default: { push, currentRoute } }))

const ok = async (config: InternalAxiosRequestConfig): Promise<AxiosResponse> => ({
  data: {},
  status: 200,
  statusText: 'OK',
  headers: {},
  config,
})

describe('API requests', () => {
  let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

  beforeEach(() => {
    localStorage.clear()
    push.mockReset()
    setActivePinia(createPinia())
    adapter = vi.fn<AxiosAdapter>(ok)
  })

  afterEach(() => {
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const load = async (mode: 'host' | 'basic') => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode, enabled: true, signInAvailable: true } }
    const http = (await import('@/http/base/axiosConfig')).default
    http.defaults.adapter = adapter
    return http
  }

  const sentHeader = (name: string) => adapter.mock.calls[0]![0].headers.get(name)

  it('sends no Authorization header in host mode, leaving the session cookie to the browser', async () => {
    const http = await load('host')

    await http.get('/time-job/time-jobs')

    expect(adapter).toHaveBeenCalledOnce()
    expect(sentHeader('Authorization')).toBeUndefined()
    expect(localStorage.length).toBe(0)
  })

  it('sends the stored Basic credential in Basic mode', async () => {
    localStorage.setItem('jobs_basic_auth', btoa('tester:fake'))
    const http = await load('basic')

    await http.get('/time-job/time-jobs')

    expect(sentHeader('Authorization')).toBe(`Basic ${btoa('tester:fake')}`)
  })

  it('marks every request with the header the server requires on state changes', async () => {
    const http = await load('host')

    await http.post('/job-host/stop')

    expect(sentHeader('X-Requested-With')).toBe('XMLHttpRequest')
  })

  it('sends the user to the login page with the current path when the session is gone', async () => {
    adapter.mockImplementation(async (config) => {
      const response = { ...(await ok(config)), status: 401, statusText: 'Unauthorized' }
      throw new AxiosError('Unauthorized', AxiosError.ERR_BAD_REQUEST, config, null, response)
    })
    const http = await load('host')
    const { useAuthStore } = await import('@/stores/authStore')
    const authStore = useAuthStore()

    await expect(http.get('/time-job/time-jobs')).rejects.toBeInstanceOf(AxiosError)

    expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'Login', query: { redirect: '/time-jobs?page=2' } })
    expect(authStore.authStatus.authenticated).toBe(false)
    expect(authStore.errorMessage).toBe('Your session has ended. Please sign in again.')
  })
})
