import { describe, expect, it } from 'vitest'
import { formatDuration, lookupCall, recordState } from '../src/api.ts'
import idempotency from '../src/module.ts'
import { records } from './fake-server.ts'

describe('idempotency module', () => {
  it('declares one Idempotency entry in the platform product with no count', () => {
    expect(idempotency.id).toBe('idempotency')
    expect(idempotency.product).toBe('platform')
    const entries = idempotency.sidebar.flatMap((section) => section.entries)
    expect(entries).toEqual([expect.objectContaining({ label: 'Idempotency', icon: 'repeat' })])
    expect(entries[0]).not.toHaveProperty('countKey')
  })

  it.each([
    ['completed', records.completed, 'Completed', 'ok'],
    ['in flight', records.inFlight, 'In flight', 'enq'],
    ['in flight past retention', records.pastRetention, 'In flight', 'enq'],
    ['stalled', records.stalled, 'Stalled', 'proc'],
    ['released', records.released, 'Released', 'off'],
    ['never admitted', records.notAdmitted, 'Not admitted yet', 'off'],
  ])('names a %s record from the served fields', (_, record, label, tone) => {
    expect(recordState(record)).toMatchObject({ label, tone })
  })

  it('judges retention against the store clock, not the browser clock', () => {
    expect(recordState(records.pastRetention).state).toBe('past-retention')
    expect(recordState({ ...records.pastRetention, observedAt: '2026-11-27T14:19:59Z' }).state).toBe('in-flight')
  })

  it('sends no tenant for a blank one, so the lookup reads the host scope', () => {
    expect(lookupCall('k', '')).toEqual({ key: 'k' })
    expect(lookupCall('k', 'acme')).toEqual({ key: 'k', tenantId: 'acme' })
  })

  it('reads durations in the largest whole unit', () => {
    expect(formatDuration(86400)).toBe('1 d')
    expect(formatDuration(3600)).toBe('1 h')
    expect(formatDuration(120)).toBe('2 min')
    expect(formatDuration(90)).toBe('90 s')
  })
})
