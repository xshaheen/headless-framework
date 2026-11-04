import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as signalR from '@microsoft/signalr'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const HUB_URL = '/jobs/dashboard/job-notification-hub'
const CREDENTIAL = btoa('tester:fake')

const { push, currentRoute } = vi.hoisted(() => ({
  push: vi.fn(),
  currentRoute: { value: { name: 'TimeJob', fullPath: '/time-jobs' } },
}))

vi.mock('@/router', () => ({ default: { push, currentRoute } }))

interface FakeConnection {
  url: string
  state: signalR.HubConnectionState
  on: ReturnType<typeof vi.fn>
  start: ReturnType<typeof vi.fn>
  stop: ReturnType<typeof vi.fn>
  onclose: ReturnType<typeof vi.fn>
  /** The server drops the connection, as it does when the session the hub connected with expires. */
  serverClose: () => Promise<void>
}

describe('notification hub lifecycle', () => {
  let built: FakeConnection[]

  beforeEach(() => {
    localStorage.clear()
    push.mockReset()
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'basic', enabled: true, signInAvailable: false } }
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
      const closeHandlers: Array<() => Promise<void> | void> = []
      const connection: FakeConnection = {
        url,
        state: signalR.HubConnectionState.Disconnected,
        on: vi.fn(),
        start: vi.fn(async () => {
          connection.state = signalR.HubConnectionState.Connected
        }),
        stop: vi.fn(async () => {
          connection.state = signalR.HubConnectionState.Disconnected
          for (const handler of closeHandlers) await handler()
        }),
        onclose: vi.fn((handler: () => Promise<void> | void) => closeHandlers.push(handler)),
        serverClose: async () => {
          connection.state = signalR.HubConnectionState.Disconnected
          for (const handler of closeHandlers) await handler()
        },
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
    await authStore.initializeAuth()
    await nextTick()
    await vi.waitFor(() => expect(connectionStore.isInitialized).toBe(true))
    const opened = built.find((connection) => connection.start.mock.calls.length > 0)!
    expect(opened.url).toContain('access_token=')

    await authStore.logout()
    await vi.waitFor(() => expect(connectionStore.isInitialized).toBe(false))
    expect(opened.stop).toHaveBeenCalled()
    // Closing it on sign-out is not a dropped connection, so nothing reconnects.
    expect(opened.start).toHaveBeenCalledOnce()
  })

  it('does not reconnect after closing the connection itself', async () => {
    localStorage.setItem('jobs_basic_auth', CREDENTIAL)
    const { default: BaseHub } = await import('@/hub/base/baseHub')
    const hub = new BaseHub()
    await hub.startConnectionAsync()

    await hub.stopConnectionAsync()

    expect(built[0]!.start).toHaveBeenCalledOnce()
  })

  describe('in host mode', () => {
    const session = (authenticated: boolean) =>
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ mode: 'host', enabled: true, authenticated, name: 'ops', canOperate: true }),
      })

    beforeEach(() => {
      window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'host', enabled: true, signInAvailable: true } }
      vi.stubGlobal('fetch', session(true))
    })

    afterEach(() => {
      vi.unstubAllGlobals()
    })

    const openSignedIn = async () => {
      const { useConnectionStore } = await import('@/stores/connectionStore')
      const { useAuthStore } = await import('@/stores/authStore')
      const connectionStore = useConnectionStore()
      const authStore = useAuthStore()
      await authStore.initializeAuth()
      await vi.waitFor(() => expect(connectionStore.isInitialized).toBe(true))
      const opened = built.find((connection) => connection.start.mock.calls.length > 0)!
      return { authStore, opened }
    }

    it('reconnects with the session cookie when the server closes the connection', async () => {
      const { opened } = await openSignedIn()

      await opened.serverClose()

      expect(opened.url).toBe(HUB_URL)
      expect(opened.start).toHaveBeenCalledTimes(2)
      expect(opened.state).toBe(signalR.HubConnectionState.Connected)
      expect(push).not.toHaveBeenCalled()
    })

    it('neither marks the hub connected nor retries when the first connection answers 401', async () => {
      const failing = built.length
      vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockImplementation(() => {
        const connection = {
          url: HUB_URL,
          state: signalR.HubConnectionState.Disconnected,
          on: vi.fn(),
          start: vi.fn().mockRejectedValue(new signalR.HttpError('Unauthorized', 401)),
          stop: vi.fn(),
          onclose: vi.fn(),
        }
        built.push(connection as unknown as FakeConnection)
        return connection as unknown as signalR.HubConnection
      })
      const { useConnectionStore } = await import('@/stores/connectionStore')
      const { useAuthStore } = await import('@/stores/authStore')
      const connectionStore = useConnectionStore()
      await useAuthStore().initializeAuth()

      await vi.waitFor(() => expect(push).toHaveBeenCalledOnce())
      await connectionStore.initializeConnectionWithRetry()

      const attempts = built.slice(failing).reduce((count, connection) => count + connection.start.mock.calls.length, 0)
      expect(attempts).toBe(1)
      expect(connectionStore.isInitialized).toBe(false)
      expect(connectionStore.connectionState).toBe('Disconnected')
    })

    it('sends the user to sign in when reconnecting answers 401', async () => {
      const { authStore, opened } = await openSignedIn()
      opened.start.mockRejectedValueOnce(new signalR.HttpError('Unauthorized', 401))

      await opened.serverClose()

      await vi.waitFor(() =>
        expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'Login', query: { redirect: '/time-jobs' } }),
      )
      expect(authStore.isLoggedIn).toBe(false)
    })
  })
})
