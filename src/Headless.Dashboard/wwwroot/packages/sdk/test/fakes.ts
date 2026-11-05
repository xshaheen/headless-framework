import type { StreamChannel, StreamLockManager, StreamSource } from '../src/stream/client.ts'

/** Web Locks for tabs in one test: one holder per name, waiters granted in order, aborts honoured. */
export class FakeLocks implements StreamLockManager {
  private readonly held = new Set<string>()
  private readonly queues = new Map<string, { callback: () => Promise<void>; resolve: () => void }[]>()

  request(name: string, options: { mode: 'exclusive'; signal: AbortSignal }, callback: () => Promise<void>) {
    return new Promise<unknown>((resolve, reject) => {
      const queue = this.queues.get(name) ?? []
      this.queues.set(name, queue)
      const entry = { callback, resolve: () => resolve(undefined) }

      options.signal.addEventListener('abort', () => {
        const at = queue.indexOf(entry)

        if (at >= 0) {
          queue.splice(at, 1)
          reject(new DOMException('Aborted', 'AbortError'))
        }
      })

      queue.push(entry)
      this.pump(name)
    })
  }

  private pump(name: string) {
    if (this.held.has(name)) {
      return
    }

    const next = this.queues.get(name)?.shift()

    if (!next) {
      return
    }

    this.held.add(name)
    void next.callback().then(() => {
      this.held.delete(name)
      next.resolve()
      this.pump(name)
    })
  }
}

/** BroadcastChannels for tabs in one test: a message reaches every other channel of the name, asynchronously. */
export class FakeChannels {
  private readonly open = new Set<{ name: string; listeners: Set<(event: { data: unknown }) => void> }>()

  create = (name: string): StreamChannel => {
    const self = { name, listeners: new Set<(event: { data: unknown }) => void>() }
    this.open.add(self)

    return {
      postMessage: (message) => {
        const data = structuredClone(message)

        for (const other of this.open) {
          if (other !== self && other.name === name) {
            queueMicrotask(() => other.listeners.forEach((listener) => listener({ data })))
          }
        }
      },
      addEventListener: (_type, listener) => self.listeners.add(listener),
      close: () => this.open.delete(self),
    }
  }
}

/** An EventSource the test feeds by hand. */
export class FakeSource implements StreamSource {
  readyState = 0
  closed = false
  private readonly listeners = new Map<string, ((event: { data?: unknown; lastEventId?: string }) => void)[]>()

  readonly url: string

  constructor(url: string) {
    this.url = url
  }

  addEventListener(type: string, listener: (event: { data?: unknown; lastEventId?: string }) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener])
  }

  close() {
    this.closed = true
    this.readyState = 2
  }

  emit(type: string, data: unknown, lastEventId = '') {
    if (type !== 'error') {
      this.readyState = 1
    }

    this.listeners.get(type)?.forEach((listener) => listener({ data: JSON.stringify(data), lastEventId }))
  }

  /** The query parameter `name` of the URL the source opened. */
  param(name: string) {
    return new URL(this.url, 'http://localhost').searchParams.get(name)
  }
}

/** Records every EventSource the tabs open. */
export class FakeSources {
  readonly opened: FakeSource[] = []

  create = (url: string) => {
    const source = new FakeSource(url)
    this.opened.push(source)

    return source
  }

  get open() {
    return this.opened.filter((source) => !source.closed)
  }
}
