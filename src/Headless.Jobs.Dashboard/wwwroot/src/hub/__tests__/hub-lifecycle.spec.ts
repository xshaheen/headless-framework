import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as signalR from '@microsoft/signalr'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const HUB_URL = '/jobs/dashboard/job-notification-hub'
const CREDENTIAL = btoa('tester:fake')

interface FakeConnection {
  url: string
  state: signalR.HubConnectionState
  on: ReturnType<typeof vi.fn>
  start: ReturnType<typeof vi.fn>
  stop: ReturnType<typeof vi.fn>
}

describe('notification hub lifecycle', () => {
  let built: FakeConnection[]

  beforeEach(() => {
    localStorage.clear()
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'basic', enabled: true, sessionTimeout: 60 } }
    setActivePinia(createPinia())
    built = []
    // Each built connection records its URL; start() marks it connected, as a real one would.
    let url = ''
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl').mockImplementation(function (
      this: signalR.HubConnectionBuilder,
      hubUrl: string,
    ) {
      url = hubUrl
      return this
    })
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockImplementation(() => {
      const connection: FakeConnection = {
        url,
        state: signalR.HubConnectionState.Disconnected,
        on: vi.fn(),
        start: vi.fn(async () => {
          connection.state = signalR.HubConnectionState.Connected
        }),
        stop: vi.fn(async () => {
          connection.state = signalR.HubConnectionState.Disconnected
        }),
      }
      built.push(connection)
      return connection as unknown as signalR.HubConnection
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.resetModules()
    localStorage.clear()
    delete window.JobsConfig
  })

  it('connects with the credential stored after the hub was built, keeping its subscriptions', async () => {
    const { default: BaseHub } = await import('@/hub/base/baseHub')
    const hub = new BaseHub()
    const callback = vi.fn()
    hub.onReceiveMessageAsSingle('JobProgressNotification', callback)

    localStorage.setItem('jobs_basic_auth', CREDENTIAL)
    await hub.startConnectionAsync()

    expect(built).toHaveLength(2)
    expect(built[1].url).toBe(`${HUB_URL}?access_token=${encodeURIComponent(CREDENTIAL)}`)
    expect(built[1].start).toHaveBeenCalledOnce()
    expect(built[0].start).not.toHaveBeenCalled()
    const [name, handler] = built[1].on.mock.calls[0]
    expect(name).toBe('JobProgressNotification')
    handler({ id: 'job-1' })
    expect(callback).toHaveBeenCalledWith({ id: 'job-1' })
  })

  it('reuses the connection when the credential has not changed', async () => {
    localStorage.setItem('jobs_basic_auth', CREDENTIAL)
    const { default: BaseHub } = await import('@/hub/base/baseHub')
    const hub = new BaseHub()

    await hub.startConnectionAsync()

    expect(built).toHaveLength(1)
    expect(built[0].start).toHaveBeenCalledOnce()
  })

  it('does not open the hub before sign-in, opens it at sign-in, and closes it at sign-out', async () => {
    const { useConnectionStore } = await import('@/stores/connectionStore')
    const { useAuthStore } = await import('@/stores/authStore')
    const connectionStore = useConnectionStore()
    const authStore = useAuthStore()
    await authStore.initializeAuth()

    await connectionStore.initializeConnectionWithRetry()
    expect(built.some((connection) => connection.start.mock.calls.length > 0)).toBe(false)

    localStorage.setItem('jobs_basic_auth', CREDENTIAL)
    localStorage.setItem('jobs_signed_in_at', String(Date.now()))
    await authStore.initializeAuth()
    await nextTick()
    await vi.waitFor(() => expect(connectionStore.isInitialized).toBe(true))
    const opened = built.find((connection) => connection.start.mock.calls.length > 0)!
    expect(opened.url).toContain('access_token=')

    await authStore.logout()
    await vi.waitFor(() => expect(opened.stop).toHaveBeenCalled())
    expect(connectionStore.isInitialized).toBe(false)
  })
})
