import type { Component } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import { useAuthStore } from '@/stores/authStore'
import { httpService } from '@/services/http'
import Nodes from '@/views/Nodes.vue'
import Scheduled from '@/views/Scheduled.vue'

type Mode = 'none' | 'basic' | 'apikey' | 'host'

const NODE = { name: 'node-a', address: 'http://10.0.0.5', port: 8080, tags: '' }

const SCHEDULED_ROW = {
  storageId: '42',
  messageId: 'm-1',
  messageName: 'orders.placed',
  lane: 'Bus',
  expectedDueAt: '2026-11-04T12:00:00.0000000Z',
  status: 'Pending',
  isLeased: false,
  owner: null,
  lockedUntil: null,
  inlineAttempts: 0,
}

const configure = (mode: Mode) => {
  window.MessagingConfig = {
    basePath: '/messaging/dashboard',
    auth: { mode, enabled: mode !== 'none', signInAvailable: mode === 'host' },
  }
}

// Mounts a page as an already signed-in session, so the test controls only whether the user may operate. Plugins
// install after Pinia, so the store seeded here is the one the page reads.
const mountSignedIn = (component: Component, canOperate: boolean): Mounted =>
  mountWithApp(component, [
    {
      install() {
        const store = useAuthStore()
        store.isInitialized = true
        store.authStatus = { authenticated: true, username: 'ops', canOperate }
      },
    },
  ])

const buttonsLabelled = (label: string) =>
  [...document.querySelectorAll('button')].filter((b) => b.textContent?.trim() === label)

const buttonsTitled = (title: string) => [...document.querySelectorAll(`button[title="${title}"]`)]

describe('node switching and ping', () => {
  let mounted: Mounted | undefined

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.restoreAllMocks()
    delete window.MessagingConfig
  })

  const stubNodes = () =>
    vi.spyOn(httpService, 'get').mockImplementation(async (endpoint: string) => {
      if (endpoint === '/list-ns') return []
      if (endpoint === '/nodes') return [NODE]
      throw new Error(`Unexpected request ${endpoint}`)
    })

  it('hides Switch in host mode, where the server never relays to another node', async () => {
    configure('host')
    stubNodes()

    mounted = mountWithApp(Nodes)
    await flush()

    expect(document.body.textContent).toContain('node-a')
    expect(buttonsLabelled('Switch')).toHaveLength(0)
  })

  it('keeps Switch outside host mode', async () => {
    configure('none')
    stubNodes()

    mounted = mountWithApp(Nodes)
    await flush()

    expect(buttonsLabelled('Switch')).toHaveLength(1)
  })

  it('sends ping as a POST with the endpoint in the query', async () => {
    configure('none')
    stubNodes()
    const post = vi.spyOn(httpService, 'post').mockResolvedValue('12')

    mounted = mountWithApp(Nodes)
    await flush()
    ;(buttonsLabelled('Ping')[0] as HTMLButtonElement).click()
    await flush()

    expect(post).toHaveBeenCalledWith(
      `/ping?endpoint=${encodeURIComponent('http://10.0.0.5:8080')}`,
    )
    expect(document.body.textContent).toContain('12ms')
  })
})

describe('operator actions', () => {
  let mounted: Mounted | undefined

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.restoreAllMocks()
    delete window.MessagingConfig
  })

  const stubScheduled = () =>
    vi.spyOn(httpService, 'get').mockResolvedValue({
      items: [SCHEDULED_ROW],
      index: 1,
      size: 20,
      totalItems: 1,
      totalPages: 1,
      hasPrevious: false,
      hasNext: false,
    })

  it('hides ping when the signed-in user may not operate', async () => {
    configure('host')
    vi.spyOn(httpService, 'get').mockImplementation(async (endpoint: string) =>
      endpoint === '/nodes' ? [NODE] : [],
    )

    mounted = mountSignedIn(Nodes, false)
    await flush()

    expect(document.body.textContent).toContain('node-a')
    expect(buttonsLabelled('Ping')).toHaveLength(0)
    expect(buttonsLabelled('Ping All')).toHaveLength(0)
  })

  it('hides Revoke and Dispatch now when the signed-in user may not operate', async () => {
    configure('host')
    stubScheduled()

    mounted = mountSignedIn(Scheduled, false)
    await flush()

    expect(document.body.textContent).toContain('orders.placed')
    expect(buttonsTitled('Revoke')).toHaveLength(0)
    expect(buttonsTitled('Dispatch now')).toHaveLength(0)
  })

  it('shows Revoke and Dispatch now to a user who may operate', async () => {
    configure('host')
    stubScheduled()

    mounted = mountSignedIn(Scheduled, true)
    await flush()

    expect(buttonsTitled('Revoke')).toHaveLength(1)
    expect(buttonsTitled('Dispatch now')).toHaveLength(1)
  })
})
