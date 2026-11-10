import { describe, expect, it } from 'vitest'
import { readRunProjection } from '../src/runs.ts'

describe('run projection', () => {
  it('reads no projection when the change carries none', () => {
    expect(readRunProjection(null)).toBeUndefined()
    expect(readRunProjection(undefined)).toBeUndefined()
    expect(readRunProjection('TimeJob')).toBeUndefined()
    expect(readRunProjection([])).toBeUndefined()
  })

  it('keeps the deciding fields that hold their served types', () => {
    expect(
      readRunProjection({
        id: 'run-1',
        cronJobId: 'cron-1',
        kind: 'CronJobOccurrence',
        status: 'Queued',
        executionTime: '2026-11-05T12:00:00Z',
      }),
    ).toEqual({
      cronJobId: 'cron-1',
      kind: 'CronJobOccurrence',
      status: 'Queued',
      executionTime: '2026-11-05T12:00:00Z',
    })
  })

  it('drops a field of the wrong type or an unknown value, so it decides nothing', () => {
    expect(
      readRunProjection({ cronJobId: 7, kind: 'Webhook', status: 'Paused', executionTime: 1_700_000_000 }),
    ).toEqual({
      cronJobId: undefined,
      kind: undefined,
      status: undefined,
      executionTime: undefined,
    })
  })
})
