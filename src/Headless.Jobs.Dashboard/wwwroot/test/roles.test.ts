import { describe, expect, it } from 'vitest'
import { nodeStatusMark, cronStatusMark } from '../src/cron.ts'
import { attemptTones, runStatusMark, runStatuses, type AttemptOutcome, type RunStatus } from '../src/runs.ts'

describe('jobs state roles', () => {
  it('maps every run status to its state role', () => {
    const expected: Record<RunStatus, string> = {
      Idle: 'sched',
      Queued: 'enq',
      InProgress: 'proc',
      Succeeded: 'ok',
      DueDone: 'ok',
      Failed: 'fail',
      Cancelled: 'off',
      Skipped: 'off',
    }

    expect(Object.fromEntries(runStatuses.map((status) => [status, runStatusMark({ status, cancelRequested: false }).tone]))).toEqual(expected)
  })

  it('maps every attempt outcome, cron state, and node state to a state role', () => {
    const outcomes: Record<AttemptOutcome, string> = { Succeeded: 'ok', Failed: 'fail', Cancelled: 'off', Lost: 'fail', Skipped: 'off', Released: 'enq' }

    expect(attemptTones).toEqual(outcomes)
    expect([cronStatusMark({ isPaused: false }).tone, cronStatusMark({ isPaused: true }).tone]).toEqual(['sched', 'off'])
    expect(['Alive', 'Suspected', 'Dead', 'Left'].map((state) => nodeStatusMark({ state }).tone)).toEqual(['ok', 'proc', 'fail', 'off'])
  })
})
