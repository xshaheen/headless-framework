import { createFakeStream } from '@headless/dashboard-sdk/testing'
import { describe, expect, it } from 'vitest'
import { pausable } from '../src/pause.ts'

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
