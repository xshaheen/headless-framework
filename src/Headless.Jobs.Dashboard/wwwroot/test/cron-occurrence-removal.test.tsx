import { screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { now, open, run } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

const cronJobId = 'c0ffee00-0000-0000-0000-000000000001'

/** The one change the server sends when a cron job is deleted with every occurrence it stored. */
function occurrencesRemoved(id: string) {
  return {
    module: 'jobs',
    kind: 'cron-occurrences',
    id,
    topic: 'jobs.cron-occurrences',
    version: '9223372036854775807',
    projection: null,
  }
}

function runRequests(requests: readonly { path: string }[]) {
  return requests.filter((request) => request.path === 'jobs/runs').length
}

describe('jobs cron occurrence removal', () => {
  it('drops a deleted cron job’s occurrences from an open run list without reading it again', async () => {
    const view = open(
      {
        runs: [
          run('Succeeded', { id: 'occ-1', kind: 'CronJobOccurrence', cronJobId, function: 'reports.nightly' }),
          run('Failed', { id: 'occ-2', kind: 'CronJobOccurrence', cronJobId, function: 'reports.nightly' }),
          run('Failed', { id: 'job-1', function: 'billing.charge' }),
        ],
      },
      { path: 'runs' },
    )

    await waitFor(() => expect(screen.getAllByRole('row', { name: /reports\.nightly/ })).toHaveLength(2))
    const header = screen.getByRole('heading', { level: 1, name: 'All runs' }).closest('header')!
    await waitFor(() => expect(within(header).getByText('3')).toBeTruthy())
    expect(view.stream.topics()).toContain('jobs.cron-occurrences')
    const before = runRequests(view.requests)

    view.stream.push('change', occurrencesRemoved(cronJobId))

    await waitFor(() => expect(screen.queryAllByRole('row', { name: /reports\.nightly/ })).toHaveLength(0))
    expect(screen.getByRole('row', { name: /billing\.charge/ })).toBeTruthy()
    expect(within(header).getByText('1')).toBeTruthy()
    expect(runRequests(view.requests)).toBe(before)
  })

  it('keeps every row when another cron job’s occurrences are removed', async () => {
    const view = open(
      {
        runs: [run('Succeeded', { id: 'occ-1', kind: 'CronJobOccurrence', cronJobId, function: 'reports.nightly' })],
      },
      { path: 'runs' },
    )

    await screen.findByRole('row', { name: /reports\.nightly/ })

    view.stream.push('change', occurrencesRemoved('c0ffee00-0000-0000-0000-000000000002'))

    await vi.advanceTimersByTimeAsync(50)
    expect(screen.getByRole('row', { name: /reports\.nightly/ })).toBeTruthy()
  })
})
