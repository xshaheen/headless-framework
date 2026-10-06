import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { CanceledError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios'

const SIGNED_IN = new Date('2026-05-01T10:00:00Z').getTime()
const MINUTE = 60_000

const { push, currentRoute } = vi.hoisted(() => ({
  push: vi.fn(),
  currentRoute: { value: { name: 'TimeJob', fullPath: '/time-jobs' } },
}))

vi.mock('@/router', () => ({ default: { push, currentRoute } }))

describe('API request session check', () => {
  let adapter: ReturnType<typeof vi.fn<AxiosAdapter>>

  beforeEach(() => {
    vi.useFakeTimers({ now: SIGNED_IN, toFake: ['Date'] })
    localStorage.clear()
    push.mockReset()
    window.JobsConfig = {
      basePath: '/jobs/dashboard',
      auth: { mode: 'custom', enabled: true, sessionTimeout: 15 },
    }
    localStorage.setItem('jobs_custom_credential', 'Token raw-value')
    localStorage.setItem('jobs_signed_in_at', String(SIGNED_IN))
    setActivePinia(createPinia())
    adapter = vi.fn<AxiosAdapter>(async (config: InternalAxiosRequestConfig) => ({
      data: {},
      status: 200,
      statusText: 'OK',
      headers: {},
      config,
    }))
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  const load = async () => {
    const http = (await import('@/http/base/axiosConfig')).default
    http.defaults.adapter = adapter
    return http
  }

  it('sends a custom credential verbatim as the Authorization header while the session is fresh', async () => {
    const http = await load()

    await http.get('/time-job/time-jobs')

    expect(adapter).toHaveBeenCalledOnce()
    expect(adapter.mock.calls[0]![0].headers.get('Authorization')).toBe('Token raw-value')
    expect(push).not.toHaveBeenCalled()
  })

  it('cancels the request and sends the user to the login page once the session has expired', async () => {
    const http = await load()
    vi.setSystemTime(SIGNED_IN + 15 * MINUTE)

    await expect(http.get('/time-job/time-jobs')).rejects.toBeInstanceOf(CanceledError)

    expect(adapter).not.toHaveBeenCalled()
    expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'Login', query: { redirect: '/time-jobs' } })
    expect(localStorage.getItem('jobs_custom_credential')).toBeNull()
    const { useAuthStore } = await import('@/stores/authStore')
    expect(useAuthStore().errorMessage).toBe('Session expired. Please log in again.')
  })
})
