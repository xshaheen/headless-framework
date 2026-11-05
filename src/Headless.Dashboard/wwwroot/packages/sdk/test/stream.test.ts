import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createStreamClient, type StreamClient } from '../src/stream/client.ts'
import { FakeChannels, FakeLocks, FakeSources } from './fakes.ts'

describe('stream client', () => {
  let locks: FakeLocks
  let channels: FakeChannels
  let sources: FakeSources
  const tabs: StreamClient[] = []

  function openTab(onSignedOut?: () => void) {
    const tab = createStreamClient({
      url: '/headless/api/stream',
      scope: '/headless:app',
      locks,
      createChannel: channels.create,
      createSource: sources.create,
      onSignedOut,
    })
    tabs.push(tab)

    return tab
  }

  beforeEach(() => {
    vi.useFakeTimers()
    locks = new FakeLocks()
    channels = new FakeChannels()
    sources = new FakeSources()
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
