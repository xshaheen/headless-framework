import { screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { change, cronJob, now, open, run, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

const figures = [
  {
    id: 'jobs.overview.succeeded-share',
    label: 'Runs that succeeded, last 24 h',
    value: 98.2,
    unit: '%',
    comparison: { label: 'previous 24 h', value: 99.3, higherIsBetter: true },
  },
  { id: 'jobs.overview.failed', label: 'Failed runs, last 24 h', value: 23, comparison: { label: 'previous 24 h', value: 20 } },
  { id: 'jobs.overview.late', label: 'Runs that started late, last 24 h', value: 0, comparison: { label: 'previous 24 h', value: 0 } },
]

describe('jobs dashboard', () => {
  it('opens at the module’s home, and its strip shows exactly the served counts and figures', async () => {
    open({ counts: { Idle: 37, Queued: 12, InProgress: 4, Failed: 23 }, figures })

    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' })).toBeTruthy()
    const strip = screen.getByRole('region', { name: 'Jobs at a glance' })

    await waitFor(() =>
      expect([...strip.children].map((card) => card.textContent)).toEqual([
        'Idle 37',
        'Queued 12',
        'In progress 4',
        'Succeeded, 24 h 98.2 % −1.1 pt vs previous 24 h',
        'Failed, 24 h 23 +3 vs previous 24 h',
        'Started late, 24 h 0 ±0 vs previous 24 h',
      ]),
    )
    expect(within(strip).getByRole('link', { name: 'Idle 37' }).getAttribute('href')).toBe('/jobs/runs?status=Idle')
    expect(screen.queryByText(/Throughput|History|Workers busy|Queue lag/)).toBeNull()
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('reads a count it has not got as an en dash, never 0', async () => {
    open({ extra: (request) => (request.path === 'counts' ? new Promise(() => undefined) : undefined) })

    const strip = await screen.findByRole('region', { name: 'Jobs at a glance' })
    expect(within(strip).getByRole('link', { name: 'Idle –' })).toBeTruthy()
  })

  it('shows a run that moves to In progress through the stream in Processing now, without a reload', async () => {
    const queued = run('Queued', { function: 'export.ledger', node: 'node-c' })
    const running = run('InProgress', { function: 'sandbox.progress', node: 'node-a', progressPercent: 48, progressMessage: 'import: step 58 of 120' })
    const runs = [queued, running]
    const view = open({ runs })

    const card = await screen.findByRole('region', { name: 'Processing now' })
    await within(card).findByText('sandbox.progress')
    expect(within(card).getByRole('progressbar', { name: 'Progress of sandbox.progress' })).toBeTruthy()
    expect(within(card).queryByText('export.ledger')).toBeNull()
    expect(view.requests.find((request) => request.path === 'jobs/runs')?.query?.status).toBe('InProgress')

    const moved = { ...queued, status: 'InProgress' as const, version: '2' }
    runs[0] = moved
    view.stream.push('change', change(moved))

    expect(await within(card).findByText('export.ledger')).toBeTruthy()
    expect(within(card).getByText('No progress reported')).toBeTruthy()
    expect(within(card).getAllByRole('button', { name: 'Cancel' })).toHaveLength(2)
  })

  it('drops a run that finished from Processing now', async () => {
    const running = run('InProgress', { function: 'sandbox.progress' })
    const view = open({ runs: [running] })

    const card = await screen.findByRole('region', { name: 'Processing now' })
    await within(card).findByText('sandbox.progress')

    view.stream.push('change', change({ ...running, status: 'Succeeded', version: '2' }))

    expect(await within(card).findByText('Nothing is running. A due run starts once a node picks it up.')).toBeTruthy()
  })

  it('lists the next recurring runs soonest first, leaving out paused ones', async () => {
    open({
      cronJobs: [
        cronJob({ id: 'c1', function: 'reports.daily', expression: '0 6 * * *', nextDueUtc: '2026-11-06T06:00:00Z' }),
        cronJob({ id: 'c2', function: 'sandbox.progress', expression: '*/30 * * * * *', nextDueUtc: '2026-11-05T10:00:06Z' }),
        cronJob({ id: 'c3', function: 'cleanup.sessions', isPaused: true, nextDueUtc: '2026-11-05T10:00:01Z' }),
      ],
    })

    const card = await screen.findByRole('region', { name: 'Next recurring runs' })
    await waitFor(() => expect(within(card).getAllByRole('listitem').map((item) => item.querySelector('a')?.textContent)).toEqual(['sandbox.progress', 'reports.daily']))
    expect(within(card).getByRole('link', { name: 'All 3' }).getAttribute('href')).toBe('/jobs/cron-jobs')
    expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' }).closest('header')?.textContent).toContain('Next execution in 6 seconds · sandbox.progress')
  })

  it('says there are no recurring jobs yet and offers Add recurring job', async () => {
    const actor = user()
    const view = open({ cronJobs: [] })

    const card = await screen.findByRole('region', { name: 'Next recurring runs' })
    await within(card).findByText(/No recurring jobs yet/)
    await actor.click(within(card).getByRole('button', { name: 'Add recurring job' }))

    await waitFor(() => expect(`${view.location().pathname}${view.location().search}`).toBe('/jobs/cron-jobs?create=cron-job'))
  })

  it('lists the Jobs attention items, a failure group leading to its failed runs', async () => {
    open({
      attention: [
        {
          id: 'failed:billing.charge:v1:card',
          groupKey: 'billing.charge:v1:card',
          title: '3 failed runs of billing.charge',
          severity: 'critical',
          count: 3,
          capped: false,
          recommended: { command: {}, filter: { functions: ['billing.charge'], fingerprint: 'v1:card', actionableOnly: true } },
        },
        {
          id: 'unclaimed:mail.send',
          groupKey: 'unclaimed:mail.send',
          title: 'No live node runs mail.send',
          severity: 'warning',
          count: 1,
          capped: false,
          target: { module: 'jobs', kind: 'function', id: 'mail.send' },
        },
      ],
    })

    const card = await screen.findByRole('region', { name: 'Needs attention' })
    expect((await within(card).findByRole('link', { name: '3 failed runs of billing.charge' })).getAttribute('href')).toBe(
      '/jobs/runs?status=Failed&function=billing.charge&fingerprint=v1%3Acard',
    )
    expect(within(card).getByRole('link', { name: 'No live node runs mail.send' }).getAttribute('href')).toBe('/jobs/functions/mail.send')
  })

  it('opens the add form on the runs list from Add time job', async () => {
    const view = open({})

    await user().click(await screen.findByRole('button', { name: 'Add time job' }))

    expect(await screen.findByRole('complementary', { name: 'Add time job' })).toBeTruthy()
    expect(view.location().pathname).toBe('/jobs/runs')
  })
})
