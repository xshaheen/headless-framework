/** Where the browser's live stream stands, as the live indicator shows it. */
export type StreamStatus = 'connecting' | 'live' | 'stale'

/** One changed object, as the server's `change` event carries it. */
export interface StreamChange {
  module: string
  kind: string
  id: string
  /** The node whose own store holds the object; absent when every node shares the module's store. */
  node?: string
  /** `{module}.{kind}`, or `{module}.{kind}@{node}` for an object with a node: the topic the change was sent on. */
  topic: string
  /** The object's version; a change whose version the view already holds, or an older one, is ignored. */
  version: string
  /** The slim projection a list row patches itself with, already redacted on the server; absent to refetch. */
  projection?: Record<string, unknown> | null
}

/** The events a view can listen to. `heartbeat` only keeps the stream from reading as stale. */
export interface StreamEventMap {
  /** The stream reopened: `topics` are live now, and `added` are the ones that were not live before. */
  subscribed: { topics: string[]; added: string[] }
  /** Events were missed: every view refetches. */
  reset: Record<string, never>
  change: StreamChange
  'attention.changed': { module: string }
  /** `unavailable` names the sources the module's reads could not reach while it stays live. */
  'module.stale': { module: string; stale: boolean; unavailable?: string[] }
  /** One node's data, on a stream subscribed to a `{module}.{kind}@{node}` topic, went out of date or recovered. */
  'node.stale': { module: string; node: string; stale: boolean }
  /** One node's views refetch. */
  'node.reset': { module: string; node: string }
}

export type StreamEventName = keyof StreamEventMap

/** The live stream a view subscribes through: the shell's shared client, or a fake in tests. */
export interface DashboardStream {
  /** Adds topics (`{module}.{kind}`, or `{module}.{kind}@{node}`) this tab needs; returns the call that removes them again. */
  subscribe(topics: readonly string[]): () => void
  on<K extends StreamEventName>(type: K, handler: (data: StreamEventMap[K]) => void): () => void
  getStatus(): StreamStatus
  /** When the stream went stale, in `Date.now()` milliseconds; `null` while it is not stale. */
  getStaleSince(): number | null
  onStatusChange(listener: () => void): () => void
  /** Asks for the stream to be opened again now, as the `Reconnect` control does. */
  reconnect(): void
}
