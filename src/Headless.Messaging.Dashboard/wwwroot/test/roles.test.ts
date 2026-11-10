import { describe, expect, it } from 'vitest'
import { canScope, circuitTones, nodeMark, operationScope, parseFailureGroup, failureGroupPath, readFilter, rowMark } from '../src/messages.ts'
import messaging from '../src/module.ts'
import { lastMinuteRate } from '../src/parts.tsx'
import { spanSeconds } from '../src/views/consumers.tsx'
import { parseUnknownLane } from '../src/views/unknown-lanes.tsx'

describe('messaging roles', () => {
  it('marks a row by its state: held over status, a failure with a retry pending as retrying', () => {
    expect(rowMark({ status: 'Failed', terminal: true })).toEqual({ tone: 'fail', label: 'Failed' })
    expect(rowMark({ status: 'Failed', terminal: false })).toEqual({ tone: 'proc', label: 'Retrying' })
    expect(rowMark({ status: 'Failed', terminal: true, isHeld: true })).toEqual({ tone: 'off', label: 'Held' })
    expect(rowMark({ status: 'Succeeded', terminal: false })).toEqual({ tone: 'ok', label: 'Succeeded' })
    expect(rowMark({ status: 'Scheduled', terminal: false })).toEqual({ tone: 'sched', label: 'Scheduled' })
    expect(rowMark({ status: 'Queued', terminal: false })).toEqual({ tone: 'enq', label: 'Queued' })
    expect(rowMark({ terminal: false })).toEqual({ tone: 'off', label: 'Unknown' })
    expect(rowMark({ status: 'Failed', terminal: true }, 'Re-executing…')).toEqual({ tone: 'fail', label: 'Re-executing…' })
  })

  it('names a node’s state as the last forwarded call found it', () => {
    expect(nodeMark({ isLocal: true })).toEqual({ tone: 'ok', label: 'This node' })
    expect(nodeMark({ isLocal: false })).toEqual({ tone: 'sched', label: 'Not called yet' })
    expect(nodeMark({ isLocal: false, state: 'not-answering' })).toEqual({ tone: 'fail', label: 'Not answering' })
    expect(nodeMark({ isLocal: false, state: 'key-rejected' }).label).toBe('Key rejected')
    expect(nodeMark({ isLocal: false, state: 'policy-denied' }).label).toBe('Policy denied')
    expect(nodeMark({ isLocal: false, state: 'view-only' })).toEqual({ tone: 'proc', label: 'View only' })
    expect(nodeMark({ isLocal: false, state: 'insecure-peer' }).label).toBe('Insecure peer')
  })

  it('tones each circuit state', () => {
    expect(circuitTones).toEqual({ Closed: 'ok', Open: 'fail', HalfOpen: 'proc' })
  })

  it('reads a .NET time span in seconds, and nothing else as one', () => {
    expect(spanSeconds('00:00:45')).toBe(45)
    expect(spanSeconds('01:02:03.5')).toBe(3_723.5)
    expect(spanSeconds('2.00:00:00')).toBe(172_800)
    expect(spanSeconds('soon')).toBeUndefined()
  })

  it('reads an unknown-lane report’s node, store, and kind, the node holding a colon', () => {
    expect(parseUnknownLane('unknown-lane:primary:received')).toEqual({ node: '', store: 'primary', kind: 'received' })
    expect(parseUnknownLane('host:8080:unknown-lane:orders:published')).toEqual({ node: 'host:8080', store: 'orders', kind: 'published' })
    expect(parseUnknownLane('failed:received:v1:x:primary')).toBeUndefined()
  })

  it('reads a failure group, its fingerprint and consumer holding colons, and opens its failed rows on its node', () => {
    const group = parseFailureGroup('node-b:failed:received:v1:9f2c41:orders:billing-handler:primary')

    expect(group).toEqual({ node: 'node-b', kind: 'received', fingerprint: 'v1:9f2c41', consumer: 'orders:billing-handler', store: 'primary' })
    expect(failureGroupPath(group!)).toBe('received?status=Failed&terminal=true&fingerprint=v1%3A9f2c41&consumer=orders%3Abilling-handler&node=node-b')
    expect(parseFailureGroup('failed:published:v1:expired::orders')).toEqual({
      node: '',
      kind: 'published',
      fingerprint: 'v1:expired',
      consumer: '',
      store: 'orders',
    })
    expect(parseFailureGroup('failed:queued:v1:x:y:z')).toBeUndefined()
    expect(parseFailureGroup('failed:received:9f2c41:Billing:primary')).toBeUndefined()
  })

  it('reads only measured seconds of the last minute as a rate, and none as no rate', () => {
    expect(lastMinuteRate([])).toBeUndefined()
    expect(lastMinuteRate([null, null])).toBeUndefined()
    expect(lastMinuteRate([...Array.from({ length: 60 }, () => 100), 2, null, 4])).toBeCloseTo((57 * 100 + 6) / 59)
  })

  it('keeps retrying and held out of an Operation’s scope, which cannot say them', () => {
    const retrying = readFilter(new URLSearchParams('retrying=true&status=Failed'), 'received')

    expect(retrying.status).toBe('')
    expect(canScope(retrying, 'received')).toBe(false)
    expect(canScope(readFilter(new URLSearchParams('held=true'), 'received'), 'received')).toBe(false)
    // An Operation runs on one store, so a published list over every store cannot select all its matches.
    expect(canScope(readFilter(new URLSearchParams('status=failed'), 'published'), 'published')).toBe(false)
    expect(canScope(readFilter(new URLSearchParams('status=failed&store=orders'), 'published'), 'published')).toBe(true)
    expect(operationScope(readFilter(new URLSearchParams('status=failed&terminal=true&store=orders'), 'published'), 'node-b')).toEqual({
      node: 'node-b',
      store: 'orders',
      status: 'Failed',
      terminalOnly: true,
    })
  })

  it('registers every sidebar entry with the count key its list counts', () => {
    const entries: readonly { id: string; label: string; route: string; countKey?: string }[] = messaging.sidebar.flatMap(
      (section): readonly { id: string; label: string; route: string; countKey?: string }[] => section.entries,
    )

    expect(entries.map((entry) => entry.label)).toEqual([
      'Dashboard',
      'All published',
      'Succeeded',
      'Failed',
      'Retrying',
      'Delayed',
      'Scheduled',
      'Queued',
      'Scheduled deliveries',
      'All received',
      'Succeeded',
      'Failed',
      'Retrying',
      'Held',
      'Consumers',
      'Contracts',
      'Nodes',
      'Unknown lanes',
      'Operator log',
    ])
    expect(entries.find((entry) => entry.id === 'messaging.nav.received-failed')).toMatchObject({
      route: 'received?status=Failed&terminal=true',
      countKey: 'received.failed',
    })
  })
})
