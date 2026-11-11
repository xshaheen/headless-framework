import type { DashboardStream, StreamChange, StreamEventMap, StreamEventName, StreamStatus } from './types.ts'

/** The part of the Web Locks API the client uses: `navigator.locks` in a browser, a fake in tests. */
export interface StreamLockManager {
  request(name: string, options: { mode: 'exclusive'; signal: AbortSignal }, callback: () => Promise<void>): Promise<unknown>
}

/** The part of `BroadcastChannel` the client uses. */
export interface StreamChannel {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
  close(): void
}

/** The part of `EventSource` the client uses. */
export interface StreamSource {
  readonly readyState: number
  addEventListener(type: string, listener: (event: { data?: unknown; lastEventId?: string }) => void): void
  close(): void
}

export interface StreamClientOptions {
  /** The stream endpoint's absolute path, from the page state: `{base}/api/stream`. */
  url: string
  /**
   * Scopes the leader lock and the relay channel, so two dashboards on one origin (another base path or app) never
   * share a stream: the base path plus the app name.
   */
  scope: string
  /**
   * Names the channel sign-out travels on. It is wider than `scope`: a tab of the same browser session opened under
   * other permissions or another display name holds its own stream, and still must hear the sign-out and close it.
   */
  sessionChannel: string
  /**
   * Called in every tab of the session once any tab signs out (`signed-out`), or once the stream's reconnect is
   * refused with `401` because the session expired or was revoked (`session-ended`); the shell routes to its sign-in
   * page.
   */
  onSignedOut?: (reason: 'signed-out' | 'session-ended') => void
  locks?: StreamLockManager
  createChannel?: (name: string) => StreamChannel
  createSource?: (url: string) => StreamSource
  /** How long topic changes settle before the leader reopens the stream. */
  debounceMs?: number
  /** How long without any event, heartbeats included, before the stream reads as stale. */
  staleAfterMs?: number
  /**
   * How long a topic stays subscribed after its last view lets go, so a route change that drops and takes it back
   * does not reopen the stream twice. Defaults to 5 s.
   */
  graceMs?: number
  /** Draws the back-off jitter, a number in `[0, 1)`; `Math.random` unless a test fixes it. */
  random?: () => number
  /**
   * Asks the stream endpoint once, after the browser gave up on a connection, for the status it answered, since an
   * `EventSource` never exposes it: `401` ends the session, anything else backs off and retries. Resolves `0` when
   * the request itself failed.
   */
  probe?: (url: string) => Promise<number>
}

/** The shared stream, plus the calls only the shell makes. */
export interface StreamClient extends DashboardStream {
  /** Whether this tab holds the browser's one connection. */
  isLeader(): boolean
  /** Signs every tab of this browser session out of the stream: each closes it and calls `onSignedOut`. */
  signOut(): void
  /** Leaves the stream for good, handing the connection to another tab when this one held it. */
  close(): void
}

const eventNames = ['subscribed', 'reset', 'change', 'attention.changed', 'module.stale', 'node.stale', 'node.reset', 'heartbeat'] as const
type WireEventName = (typeof eventNames)[number]

// EventSource.CLOSED: the browser gave up on the connection (a non-200 answer) and will not retry by itself.
const sourceClosed = 2
const maxRetryMs = 30_000

/** The status the stream endpoint answers, read from the headers alone: a live stream's body is never waited for. */
async function probeStatus(url: string) {
  const abort = new AbortController()
  // A probe that gets no answer must not hold the retry back until the browser's own network timeout.
  const timer = setTimeout(() => abort.abort(), 5_000)

  try {
    return (await fetch(url, { credentials: 'include', signal: abort.signal })).status
  } catch {
    return 0
  } finally {
    clearTimeout(timer)
    abort.abort()
  }
}

type RelayMessage =
  | { type: 'topics'; tab: string; topics: string[] }
  | { type: 'bye'; tab: string }
  | { type: 'leader'; tab: string }
  | { type: 'event'; name: WireEventName; data: unknown; id: string }
  | { type: 'reconnect' }

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

function isRelayMessage(value: unknown): value is RelayMessage {
  if (!isRecord(value)) {
    return false
  }

  switch (value.type) {
    case 'topics':
      return typeof value.tab === 'string' && isStringArray(value.topics)
    case 'bye':
    case 'leader':
      return typeof value.tab === 'string'
    case 'event':
      return (eventNames as readonly unknown[]).includes(value.name) && typeof value.id === 'string'
    case 'reconnect':
      return true
    default:
      return false
  }
}

function isChange(value: unknown): value is StreamChange {
  return (
    isRecord(value) &&
    typeof value.module === 'string' &&
    typeof value.kind === 'string' &&
    typeof value.id === 'string' &&
    typeof value.topic === 'string' &&
    typeof value.version === 'string' &&
    (value.projection === undefined || value.projection === null || isRecord(value.projection))
  )
}

function parse(data: unknown): unknown {
  if (typeof data !== 'string') {
    return data
  }

  try {
    return JSON.parse(data) as unknown
  } catch {
    return undefined
  }
}

function defaultLocks(): StreamLockManager {
  if (typeof navigator === 'undefined' || !navigator.locks) {
    // The session cookie already requires a secure context, where Web Locks always exists.
    throw new Error('The dashboard stream needs the Web Locks API, which a secure context (HTTPS or localhost) has.')
  }

  return navigator.locks as unknown as StreamLockManager
}

/**
 * Opens the browser's one live stream. Tabs elect a leader through a Web Lock: only the leader holds the
 * connection, opened with the union of every tab's topics, and relays each event to the other tabs over a
 * `BroadcastChannel`. When the leader closes, the next tab takes the lock and reopens from the last event id.
 */
export function createStreamClient(options: StreamClientOptions): StreamClient {
  const debounceMs = options.debounceMs ?? 250
  const staleAfterMs = options.staleAfterMs ?? 5_000
  const graceMs = options.graceMs ?? 5_000
  const random = options.random ?? Math.random
  const probe = options.probe ?? probeStatus
  const locks = options.locks ?? defaultLocks()
  const createChannel = options.createChannel ?? ((name) => new BroadcastChannel(name))
  const channel = createChannel(`${options.scope}:stream`)
  const session = createChannel(options.sessionChannel)
  const createSource =
    options.createSource ?? ((url) => new EventSource(url, { withCredentials: true }) as unknown as StreamSource)

  const tab = Math.random().toString(36).slice(2)
  // A topic no view holds any more stays here at count 0 for the grace period, still part of the stream.
  const ownTopics = new Map<string, number>()
  const lingering = new Map<string, ReturnType<typeof setTimeout>>()
  const peerTopics = new Map<string, string[]>()
  const handlers = new Map<StreamEventName, Set<(data: never) => void>>()
  const statusListeners = new Set<() => void>()
  const abort = new AbortController()

  let leader = false
  let closed = false
  let releaseLock: (() => void) | undefined
  let source: StreamSource | undefined
  let openKey: string | undefined
  let lastEventId: string | undefined
  let live = new Set<string>()
  let status: StreamStatus = 'connecting'
  let staleSince: number | null = null
  let staleTimer: ReturnType<typeof setTimeout> | undefined
  let reopenTimer: ReturnType<typeof setTimeout> | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let retryMs = 1_000

  function post(message: RelayMessage) {
    if (!closed) {
      channel.postMessage(message)
    }
  }

  function emit<K extends StreamEventName>(type: K, data: StreamEventMap[K]) {
    for (const handler of [...(handlers.get(type) ?? [])]) {
      ;(handler as (value: StreamEventMap[K]) => void)(data)
    }
  }

  function setStatus(next: StreamStatus) {
    if (status === next) {
      return
    }

    status = next
    staleSince = next === 'stale' ? Date.now() : null

    for (const listener of [...statusListeners]) {
      listener()
    }
  }

  function armStaleTimer() {
    clearTimeout(staleTimer)
    staleTimer = setTimeout(() => setStatus('stale'), staleAfterMs)
  }

  function ownTopicList() {
    return [...ownTopics.keys()].sort()
  }

  function union() {
    const all = new Set(ownTopics.keys())

    for (const topics of peerTopics.values()) {
      topics.forEach((topic) => all.add(topic))
    }

    return [...all].sort()
  }

  function receive(name: WireEventName, raw: unknown, id: string) {
    if (id) {
      lastEventId = id
    }

    retryMs = 1_000
    setStatus('live')
    armStaleTimer()

    const data = parse(raw)

    switch (name) {
      case 'heartbeat':
        return
      case 'subscribed': {
        const topics = isRecord(data) && isStringArray(data.topics) ? data.topics : []
        const added = topics.filter((topic) => !live.has(topic))
        live = new Set(topics)
        emit('subscribed', { topics, added })
        return
      }
      case 'reset':
        emit('reset', {})
        return
      case 'change':
        if (isChange(data)) {
          emit('change', data)
        }
        return
      case 'attention.changed':
        if (isRecord(data) && typeof data.module === 'string') {
          emit('attention.changed', { module: data.module })
        }
        return
      case 'module.stale':
      case 'node.stale':
      case 'node.reset':
        // The server sends `node` for a node event and `stale` for a stale one; the rest passes through as sent.
        if (
          isRecord(data) &&
          typeof data.module === 'string' &&
          (name === 'module.stale' || typeof data.node === 'string') &&
          (name === 'node.reset' || typeof data.stale === 'boolean') &&
          (data.unavailable === undefined || isStringArray(data.unavailable))
        ) {
          emit(name, data as never)
        }
        return
    }
  }

  function closeSource() {
    clearTimeout(retryTimer)
    source?.close()
    source = undefined
    openKey = undefined
  }

  function open(topics: string[]) {
    closeSource()
    openKey = topics.join(',')

    const query = new URLSearchParams()
    query.set('topics', openKey)

    if (lastEventId) {
      // A new EventSource cannot send Last-Event-ID itself, so the first request carries it in the query.
      query.set('lastEventId', lastEventId)
    }

    const separator = options.url.includes('?') ? '&' : '?'
    const opened = createSource(`${options.url}${separator}${query.toString()}`)
    source = opened

    for (const name of eventNames) {
      opened.addEventListener(name, (event) => {
        if (source !== opened) {
          return
        }

        const id = event.lastEventId ?? ''
        receive(name, event.data, id)
        post({ type: 'event', name, data: event.data, id })
      })
    }

    opened.addEventListener('error', () => {
      if (source !== opened || opened.readyState !== sourceClosed) {
        // The browser retries a dropped connection itself, sending Last-Event-ID.
        return
      }

      // A refused connection is not retried by the browser. After the back-off, a probe asks why: an expired session
      // is refused for good, so it ends the session, and anything else reopens from the last event id. The wait is
      // drawn from the upper half of the back-off, so the tabs of many browsers a node restart dropped neither probe
      // nor come back in the same second.
      opened.close()
      source = undefined
      openKey = undefined
      retryTimer = setTimeout(
        async () => {
          if ((await probe(options.url)) === 401) {
            endSession('session-ended')
          } else if (!source) {
            reopen(true)
          }
        },
        retryMs * (0.5 + random() / 2),
      )
      retryMs = Math.min(retryMs * 2, maxRetryMs)
    })
  }

  function reopen(force: boolean) {
    if (!leader || closed) {
      return
    }

    const topics = union()

    if (!force && source && openKey === topics.join(',')) {
      return
    }

    open(topics)
  }

  function scheduleReopen() {
    clearTimeout(reopenTimer)
    reopenTimer = setTimeout(() => reopen(false), debounceMs)
  }

  function topicsChanged() {
    if (leader) {
      scheduleReopen()
    } else {
      post({ type: 'topics', tab, topics: ownTopicList() })
    }
  }

  function becomeLeader() {
    leader = true
    // The other tabs answer with their topics, which land within the debounce before the stream opens.
    post({ type: 'leader', tab })
    scheduleReopen()
  }

  function teardown() {
    if (closed) {
      return
    }

    closed = true
    leader = false
    clearTimeout(staleTimer)
    clearTimeout(reopenTimer)
    lingering.forEach((timer) => clearTimeout(timer))
    closeSource()
    abort.abort()
    releaseLock?.()
    channel.close()
    session.close()
  }

  /** Closes the stream in every tab of the session, each routing to sign-in for `reason`. */
  function endSession(reason: 'signed-out' | 'session-ended') {
    if (!closed) {
      session.postMessage({ type: 'signed-out', reason })
    }

    signedOut(reason)
  }

  function signedOut(reason: 'signed-out' | 'session-ended') {
    if (closed) {
      return
    }

    teardown()
    options.onSignedOut?.(reason)
  }

  channel.addEventListener('message', (event) => {
    const message = event.data

    if (closed || !isRelayMessage(message)) {
      return
    }

    switch (message.type) {
      case 'topics':
        peerTopics.set(message.tab, message.topics)

        if (leader) {
          scheduleReopen()
        }
        return
      case 'bye':
        peerTopics.delete(message.tab)

        if (leader) {
          scheduleReopen()
        }
        return
      case 'leader':
        if (!leader) {
          post({ type: 'topics', tab, topics: ownTopicList() })
        }
        return
      case 'event':
        if (!leader) {
          receive(message.name, message.data, message.id)
        }
        return
      case 'reconnect':
        reopen(true)
        return
    }
  })

  session.addEventListener('message', (event) => {
    if (isRecord(event.data) && event.data.type === 'signed-out') {
      signedOut(event.data.reason === 'session-ended' ? 'session-ended' : 'signed-out')
    }
  })

  locks
    .request(
      `${options.scope}:stream-leader`,
      { mode: 'exclusive', signal: abort.signal },
      () =>
        new Promise<void>((resolve) => {
          releaseLock = resolve

          if (closed) {
            resolve()
            return
          }

          becomeLeader()
        }),
    )
    // Closing a tab that is still waiting for the lock aborts its request.
    .catch(() => undefined)

  armStaleTimer()

  return {
    subscribe(topics) {
      let changed = false

      for (const topic of topics) {
        clearTimeout(lingering.get(topic))
        lingering.delete(topic)
        changed ||= !ownTopics.has(topic)
        ownTopics.set(topic, (ownTopics.get(topic) ?? 0) + 1)
      }

      if (changed) {
        topicsChanged()
      }

      let removed = false

      return () => {
        if (removed) {
          return
        }

        removed = true

        for (const topic of topics) {
          const count = ownTopics.get(topic) ?? 0

          if (count > 1) {
            ownTopics.set(topic, count - 1)
          } else if (count === 1 && !closed) {
            // The topic leaves the stream only if no view takes it back within the grace period.
            ownTopics.set(topic, 0)
            lingering.set(
              topic,
              setTimeout(() => {
                lingering.delete(topic)
                ownTopics.delete(topic)
                topicsChanged()
              }, graceMs),
            )
          }
        }
      }
    },

    on(type, handler) {
      let set = handlers.get(type)

      if (!set) {
        set = new Set()
        handlers.set(type, set)
      }

      set.add(handler as (data: never) => void)

      return () => {
        set.delete(handler as (data: never) => void)
      }
    },

    getStatus: () => status,
    getStaleSince: () => staleSince,

    onStatusChange(listener) {
      statusListeners.add(listener)

      return () => {
        statusListeners.delete(listener)
      }
    },

    reconnect() {
      if (leader) {
        reopen(true)
      } else {
        post({ type: 'reconnect' })
      }
    },

    isLeader: () => leader,

    signOut: () => endSession('signed-out'),

    close() {
      post({ type: 'bye', tab })
      teardown()
    },
  }
}
