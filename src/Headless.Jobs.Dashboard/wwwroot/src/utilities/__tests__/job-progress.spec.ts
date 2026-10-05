import { describe, expect, it } from 'vitest'
import { Status } from '@/http/services/types/base/baseHttpResponse.types'
import {
  applyJobProgress,
  formatProgressAge,
  formatProgressPercent,
  jobProgressDisplay,
  type JobProgressNotification,
} from '@/utilities/job-progress'

const notification = (id: string): JobProgressNotification => ({
  id,
  type: 1,
  percent: 55.5,
  message: 'halfway',
  updatedAt: '2026-01-01T00:00:10Z',
})

describe('job progress', () => {
  it('formats the percent with at most one decimal', () => {
    expect(formatProgressPercent(42.46)).toBe('42.5%')
    expect(formatProgressPercent(42)).toBe('42%')
    expect(formatProgressPercent(100)).toBe('100%')
  })

  it('shows progress live while running, muted once finished, and not otherwise', () => {
    expect(jobProgressDisplay('InProgress', 10)).toBe('active')
    expect(jobProgressDisplay(Status.InProgress, 0)).toBe('active')
    for (const status of ['Done', 'DueDone', 'Failed', 'Cancelled', 'Skipped']) {
      expect(jobProgressDisplay(status, 10)).toBe('muted')
    }
    expect(jobProgressDisplay('Queued', 10)).toBe('none')
    expect(jobProgressDisplay('Idle', 10)).toBe('none')
    expect(jobProgressDisplay('InProgress', null)).toBe('none')
    expect(jobProgressDisplay('Failed', undefined)).toBe('none')
  })

  it('reads the age of the last report, treating an offset-less timestamp as UTC', () => {
    const now = Date.parse('2026-01-01T00:01:05Z')
    expect(formatProgressAge('2026-01-01T00:01:00Z', now)).toBe('updated 5s ago')
    expect(formatProgressAge('2026-01-01T00:01:00', now)).toBe('updated 5s ago')
    expect(formatProgressAge('2026-01-01T00:00:00Z', now)).toBe('updated 1m ago')
    expect(formatProgressAge('2026-01-01T00:01:10Z', now)).toBe('updated 0s ago')
    expect(formatProgressAge(null, now)).toBe('')
    expect(formatProgressAge('not a date', now)).toBe('')
  })

  it('patches the matching row, including a chained child, and ignores unknown ids', () => {
    const rows = [
      { id: 'a', progressPercent: null, children: [{ id: 'a-child', progressPercent: null }] },
      { id: 'b', progressPercent: 1 },
    ] as { id: string; progressPercent?: number | null; children?: never[] }[]

    expect(applyJobProgress(rows, notification('a-child'))).toBe(true)
    expect((rows[0].children as unknown as { progressPercent: number }[])[0]).toMatchObject({
      progressPercent: 55.5,
      progressMessage: 'halfway',
      progressUpdatedAt: '2026-01-01T00:00:10Z',
    })

    expect(applyJobProgress(rows, notification('missing'))).toBe(false)
    expect(rows[0].progressPercent).toBeNull()
    expect(rows[1].progressPercent).toBe(1)
    expect(applyJobProgress(undefined, notification('a'))).toBe(false)
  })
})
