import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createStreamClient, type StreamClient } from '../src/stream/client.ts'
import { FakeChannels, FakeLocks, FakeSources } from './fakes.ts'

describe('stream client', () => {
  let locks: FakeLocks
  let channels: FakeChannels
  let sources: FakeSources
  // What the stream endpoint answers a probe after a refused connection, and the jitter each back-off draws.
  let probeStatus: number
  let jitter: number
  const tabs: StreamClient[] = []

  function openTab(
    onSignedOut?: (reason: 'signed-out' | 'session-ended') => void,
    scope = 'headless-dashboard:/headless:mona',
    // `null` leaves the client its own probe, which fetches the endpoint.
    probe: ((url: string) => Promise<number>) | null = async () => probeStatus,
  ) {
    const tab = createStreamClient({
      url: '/headless/api/stream',
      scope,
      sessionChannel: 'headless-dashboard:/headless:session',
      locks,
      createChannel: channels.create,
      createSource: sources.create,
      onSignedOut,
      random: () => jitter,
      probe: probe ?? undefined,
    })
    tabs.push(tab)

    return tab
  }

  beforeEach(() => {
    vi.useFakeTimers()
    locks = new FakeLocks()
    channels = new FakeChannels()
    sources = new FakeSources()
    probeStatus = 503
    jitter = 0.999
  })

  afterEach(() => {
    tabs.splice(0).forEach((tab) => tab.close())
    vi.useRealTimers()
  })

  it('opens one connection for two tabs, and the follower receives the relayed events', async () => {
    const leader = openTab()
    const follower = openTab()
    const changes: string[] = []
    follower.on('change', (change) => changes.push(`${change.id}@${change.version}`))
    follower.subscribe(['diag.run'])

    await vi.advanceTimersByTimeAsync(300)

    expect(leader.isLeader()).toBe(true)
    expect(follower.isLeader()).toBe(false)
    expect(sources.opened).toHaveLength(1)
    expect(sources.opened[0]!.param('topics')).toBe('diag.run')

    sources.opened[0]!.emit(
      'change',
      { module: 'diag', kind: 'run', id: 'r1', topic: 'diag.run', version: '3', projection: { status: 'Failed' } },
      'e1:7',
    )
    await vi.advanceTimersByTimeAsync(0)

    expect(changes).toEqual(['r1@3'])
  })

  it('hands the stream to the follower within 2 s when the leader closes, resuming from the last event id', async () => {
    const leader = openTab()
    const follower = openTab()
    follower.subscribe(['diag.run'])
    await vi.advanceTimersByTimeAsync(300)

    sources.opened[0]!.emit('heartbeat', {}, 'e1:42')
    await vi.advanceTimersByTimeAsync(0)

    leader.close()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(follower.isLeader()).toBe(true)
    expect(sources.open).toHaveLength(1)
    expect(sources.open[0]!.param('lastEventId')).toBe('e1:42')
    expect(sources.open[0]!.param('topics')).toBe('diag.run')
  })

  it('reopens the stream once with the union of three tabs’ topics after 250 ms', async () => {
    const leader = openTab()
    const second = openTab()
    const third = openTab()
    await vi.advanceTimersByTimeAsync(300)
    const before = sources.opened.length

    leader.subscribe(['diag.run'])
    second.subscribe(['diag.node'])
    third.subscribe(['diag.run', 'jobs.cron'])
    await vi.advanceTimersByTimeAsync(200)

    expect(sources.opened.length).toBe(before)

    await vi.advanceTimersByTimeAsync(100)

    expect(sources.opened.length).toBe(before + 1)
    expect(sources.open).toHaveLength(1)
    expect(sources.open[0]!.param('topics')).toBe('diag.node,diag.run,jobs.cron')
  })

  it('reports only the topics that became live in subscribed', async () => {
    const leader = openTab()
    const events: { topics: string[]; added: string[] }[] = []
    leader.on('subscribed', (event) => events.push(event))
    await vi.advanceTimersByTimeAsync(300)

    sources.open[0]!.emit('subscribed', { topics: ['diag.run'] }, 'e1:1')
    sources.open[0]!.emit('subscribed', { topics: ['diag.run', 'diag.node'] }, 'e1:2')

    expect(events).toEqual([
      { topics: ['diag.run'], added: ['diag.run'] },
      { topics: ['diag.run', 'diag.node'], added: ['diag.node'] },
    ])
  })

  it('passes node stale and reset events, and a module’s unavailable sources, through as the server sent them', async () => {
    const leader = openTab()
    const seen: unknown[] = []
    leader.on('node.stale', (event) => seen.push(event))
    leader.on('node.reset', (event) => seen.push(event))
    leader.on('module.stale', (event) => seen.push(event))
    leader.subscribe(['messaging.received@node-b'])
    await vi.advanceTimersByTimeAsync(300)

    expect(sources.open[0]!.param('topics')).toBe('messaging.received@node-b')

    sources.open[0]!.emit('node.stale', { module: 'messaging', node: 'node-b', stale: true }, 'e1:1')
    sources.open[0]!.emit('node.reset', { module: 'messaging', node: 'node-b' }, 'e1:2')
    sources.open[0]!.emit('module.stale', { module: 'messaging', stale: false, unavailable: ['outbox-2'] }, 'e1:3')
    // Malformed events are dropped: a node event without its node, and a stale event without its state.
    sources.open[0]!.emit('node.reset', { module: 'messaging' }, 'e1:4')
    sources.open[0]!.emit('node.stale', { module: 'messaging', node: 'node-b' }, 'e1:5')

    expect(seen).toEqual([
      { module: 'messaging', node: 'node-b', stale: true },
      { module: 'messaging', node: 'node-b' },
      { module: 'messaging', stale: false, unavailable: ['outbox-2'] },
    ])
  })

  it('marks itself stale after 5 s without an event, and live on the next one', async () => {
    const leader = openTab()
    const follower = openTab()
    await vi.advanceTimersByTimeAsync(300)
    sources.open[0]!.emit('heartbeat', {}, 'e1:1')
    await vi.advanceTimersByTimeAsync(0)

    expect(leader.getStatus()).toBe('live')
    expect(follower.getStatus()).toBe('live')

    await vi.advanceTimersByTimeAsync(4_900)
    expect(leader.getStatus()).toBe('live')

    await vi.advanceTimersByTimeAsync(200)
    expect(leader.getStatus()).toBe('stale')
    expect(follower.getStatus()).toBe('stale')
    expect(leader.getStaleSince()).not.toBeNull()

    sources.open[0]!.emit('heartbeat', {}, 'e1:2')
    await vi.advanceTimersByTimeAsync(0)

    expect(leader.getStatus()).toBe('live')
    expect(follower.getStatus()).toBe('live')
  })

  it('closes the stream in every tab and routes each to sign-in when one tab signs out', async () => {
    const routes: string[] = []
    const leader = openTab(() => routes.push('leader'))
    const second = openTab(() => routes.push('second'))
    const third = openTab(() => routes.push('third'))
    await vi.advanceTimersByTimeAsync(300)
    expect(sources.open).toHaveLength(1)

    third.signOut()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(routes.sort()).toEqual(['leader', 'second', 'third'])
    expect(sources.open).toHaveLength(0)
    expect(leader.isLeader()).toBe(false)
    expect(second.isLeader()).toBe(false)
  })

  it('signs out a tab that holds its own stream under another scope of the same session', async () => {
    const routes: string[] = []
    const mona = openTab(() => routes.push('mona'))
    const monaViewOnly = openTab(() => routes.push('mona view-only'), 'headless-dashboard:/headless:mona-view-only')
    await vi.advanceTimersByTimeAsync(300)
    expect(mona.isLeader()).toBe(true)
    expect(monaViewOnly.isLeader()).toBe(true)
    expect(sources.open).toHaveLength(2)

    mona.signOut()
    await vi.advanceTimersByTimeAsync(0)

    expect(routes.sort()).toEqual(['mona', 'mona view-only'])
    expect(sources.open).toHaveLength(0)
    expect(monaViewOnly.isLeader()).toBe(false)
  })

  it('keeps a dropped topic for 5 s, so a route that takes it back does not reopen the stream', async () => {
    const leader = openTab()
    const drop = leader.subscribe(['diag.run'])
    await vi.advanceTimersByTimeAsync(300)
    expect(sources.opened).toHaveLength(1)

    // A route change: the old view lets go of the topic and the new one takes it again.
    drop()
    await vi.advanceTimersByTimeAsync(1_000)
    const keep = leader.subscribe(['diag.run'])
    await vi.advanceTimersByTimeAsync(10_000)

    expect(sources.opened).toHaveLength(1)

    keep()
    await vi.advanceTimersByTimeAsync(4_900)
    expect(sources.opened).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(400)
    expect(sources.opened).toHaveLength(2)
    expect(sources.open[0]!.param('topics')).toBe('')
  })

  it('keeps a follower’s dropped topic in the leader’s stream for the grace period too', async () => {
    openTab()
    const follower = openTab()
    const drop = follower.subscribe(['diag.node'])
    await vi.advanceTimersByTimeAsync(300)
    expect(sources.open[0]!.param('topics')).toBe('diag.node')
    const opened = sources.opened.length

    drop()
    await vi.advanceTimersByTimeAsync(4_000)
    expect(sources.opened).toHaveLength(opened)

    await vi.advanceTimersByTimeAsync(2_000)
    expect(sources.opened).toHaveLength(opened + 1)
    expect(sources.open[0]!.param('topics')).toBe('')
  })

  it('draws each retry from the upper half of the back-off, so dropped browsers do not return in step', async () => {
    jitter = 0
    openTab()
    await vi.advanceTimersByTimeAsync(300)

    sources.open[0]!.readyState = 2
    sources.open[0]!.emit('error', {})
    await vi.advanceTimersByTimeAsync(499)
    expect(sources.opened).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(1)
    expect(sources.opened).toHaveLength(2)

    // The next refusal doubles the back-off: with the same draw it waits 1 s, half of 2 s.
    jitter = 0.5
    sources.open[0]!.readyState = 2
    sources.open[0]!.emit('error', {})
    await vi.advanceTimersByTimeAsync(1_499)
    expect(sources.opened).toHaveLength(2)

    await vi.advanceTimersByTimeAsync(1)
    expect(sources.opened).toHaveLength(3)
  })

  it('ends the session in every tab, instead of retrying, when the reconnect is refused with 401', async () => {
    const routes: string[] = []
    openTab((reason) => routes.push(`leader ${reason}`))
    openTab((reason) => routes.push(`follower ${reason}`))
    await vi.advanceTimersByTimeAsync(300)

    probeStatus = 401
    sources.open[0]!.readyState = 2
    sources.open[0]!.emit('error', {})
    await vi.advanceTimersByTimeAsync(60_000)

    expect(routes.sort()).toEqual(['follower session-ended', 'leader session-ended'])
    expect(sources.opened).toHaveLength(1)
    expect(sources.open).toHaveLength(0)
  })

  it('reopens after a probe that gets no answer within 5 s, rather than waiting on it', async () => {
    // The endpoint accepts the probe and never answers; only the probe's own abort ends it.
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url: string, init: { signal: AbortSignal }) =>
          new Promise((_resolve, reject) => init.signal.addEventListener('abort', () => reject(new Error('aborted')))),
      ),
    )
    openTab(undefined, undefined, null)
    await vi.advanceTimersByTimeAsync(300)

    sources.open[0]!.readyState = 2
    sources.open[0]!.emit('error', {})
    await vi.advanceTimersByTimeAsync(5_900)
    expect(sources.opened).toHaveLength(1)

    await vi.advanceTimersByTimeAsync(200)
    expect(sources.opened).toHaveLength(2)
    vi.unstubAllGlobals()
  })

  it('does not open a second stream when a reconnect wins the race with a pending probe', async () => {
    let answer: (status: number) => void = () => undefined
    const leader = openTab(undefined, undefined, () => new Promise((resolve) => (answer = resolve)))
    await vi.advanceTimersByTimeAsync(300)

    sources.open[0]!.readyState = 2
    sources.open[0]!.emit('error', {})
    await vi.advanceTimersByTimeAsync(1_000)
    leader.reconnect()
    expect(sources.opened).toHaveLength(2)

    answer(503)
    await vi.advanceTimersByTimeAsync(60_000)

    expect(sources.opened).toHaveLength(2)
    expect(sources.open).toHaveLength(1)
  })

  it('reopens after a refused connection, with back-off', async () => {
    openTab()
    await vi.advanceTimersByTimeAsync(300)
    const first = sources.open[0]!

    first.readyState = 2
    first.emit('error', {})
    await vi.advanceTimersByTimeAsync(1_000)

    expect(sources.opened).toHaveLength(2)
    expect(sources.open).toHaveLength(1)
  })
})
