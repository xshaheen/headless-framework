import { createFakeStream } from '@headless/dashboard-sdk/testing'
import { describe, expect, it } from 'vitest'
import { maxHeld, pausable } from '../src/pause.ts'

const change = (id: string) => ({ module: 'jobs', kind: 'run', id, topic: 'jobs.run', version: '1' })

describe('pause', () => {
  it('holds data changes while paused and replays them in arrival order on resume', () => {
    const stream = createFakeStream()
    const live = pausable(stream)
    const seen: string[] = []
    live.on('change', (data) => seen.push(`change ${data.id}`))
    live.on('attention.changed', (data) => seen.push(`attention ${data.module}`))

    live.setPaused(true)
    stream.push('change', change('r1'))
    stream.push('attention.changed', { module: 'jobs' })
    stream.push('change', change('r2'))

    expect(seen).toEqual([])
    expect(live.getPending()).toBe(3)

    live.setPaused(false)

    expect(seen).toEqual(['change r1', 'attention jobs', 'change r2'])
    expect(live.getPending()).toBeNull()
  })

  it('holds a node reset while paused and applies it on resume, in arrival order', () => {
    const stream = createFakeStream()
    const live = pausable(stream)
    const seen: string[] = []
    live.on('change', (data) => seen.push(`change ${data.id}`))
    live.on('node.reset', (data) => seen.push(`node.reset ${data.node}`))

    live.setPaused(true)
    stream.push('change', change('r1'))
    stream.push('node.reset', { module: 'messaging', node: 'node-b' })

    expect(seen).toEqual([])
    expect(live.getPending()).toBe(2)

    live.setPaused(false)

    expect(seen).toEqual(['change r1', 'node.reset node-b'])
  })

  it('replays one reset instead of the held changes once a pause outlasts the cap, keeping the count exact', () => {
    const stream = createFakeStream()
    const live = pausable(stream)
    const seen: string[] = []
    live.on('change', (data) => seen.push(`change ${data.id}`))
    live.on('reset', () => seen.push('reset'))

    live.setPaused(true)

    for (let index = 0; index <= maxHeld; index++) {
      stream.push('change', change(`r${index}`))
    }

    stream.push('change', change('late'))

    expect(live.getPending()).toBe(maxHeld + 2)

    live.setPaused(false)

    // Every view reads again, so no change the server never resends is lost; the one after the reset still applies.
    expect(seen).toEqual(['reset', 'change late'])
  })

  it('passes a module going stale through while paused, since it is not data', () => {
    const stream = createFakeStream()
    const live = pausable(stream)
    const seen: boolean[] = []
    live.on('module.stale', (data) => seen.push(data.stale))

    live.setPaused(true)
    stream.push('module.stale', { module: 'jobs', stale: true })

    expect(seen).toEqual([true])
    expect(live.getPending()).toBe(0)
  })
})
