import { act, screen, waitFor, within } from '@testing-library/react'
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

function described(button: HTMLElement) {
  return document.getElementById(button.getAttribute('aria-describedby')!)?.textContent
}

const figures = [
  {
    id: 'jobs.overview.succeeded-share',
    label: 'Runs that succeeded, last 24 h',
    value: 98.2,
    unit: '%',
    comparison: { label: 'previous 24 h', value: 99.3, higherIsBetter: true },
  },
  {
    id: 'jobs.overview.failed',
    label: 'Failed runs, last 24 h',
    value: 23,
    comparison: { label: 'previous 24 h', value: 20 },
  },
  {
    id: 'jobs.overview.late',
    label: 'Runs that started late, last 24 h',
    value: 0,
    comparison: { label: 'previous 24 h', value: 0 },
  },
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
    // The only chart is the served runs-per-day series.
    expect(screen.getAllByRole('img').map((image) => image.getAttribute('aria-label'))).toEqual([
      expect.stringMatching(/^Runs per day by status/),
    ])
  })

  it('reads a count it has not got as an en dash, never 0', async () => {
    open({ extra: (request) => (request.path === 'counts' ? new Promise(() => undefined) : undefined) })

    const strip = await screen.findByRole('region', { name: 'Jobs at a glance' })
    expect(within(strip).getByRole('link', { name: 'Idle –' })).toBeTruthy()
  })

  it('shares the shell sidebar’s cached counts in the sidebar’s own shape, both ways', async () => {
    const view = open({ counts: { Idle: 37 } })
    const strip = await screen.findByRole('region', { name: 'Jobs at a glance' })
    await within(strip).findByRole('link', { name: 'Idle 37' })

    // The sidebar reads this entry when the strip fetched first; a flat map here throws in the shell.
    expect(view.client.getQueryData(['shell', 'counts', 'jobs'])).toEqual({
      counts: { Idle: { value: 37, capped: false } },
      stale: [],
    })

    // And the strip reads the entry the sidebar wrote when it fetched first.
    act(() => {
      view.client.setQueryData(['shell', 'counts', 'jobs'], {
        counts: { Queued: { value: 5, capped: false } },
        stale: [],
      })
    })

    expect(await within(strip).findByRole('link', { name: 'Queued 5' })).toBeTruthy()
  })

  it('shows a run that moves to In progress through the stream in Processing now, without a reload', async () => {
    const queued = run('Queued', { function: 'export.ledger', node: 'node-c' })
    const running = run('InProgress', {
      function: 'sandbox.progress',
      node: 'node-a',
      progressPercent: 48,
      progressMessage: 'import: step 58 of 120',
    })
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
    const runs = [running]
    const view = open({ runs })

    const card = await screen.findByRole('region', { name: 'Processing now' })
    await within(card).findByText('sandbox.progress')

    runs[0] = { ...running, status: 'Succeeded', version: '2' }
    view.stream.push('change', change(runs[0]))

    expect(await within(card).findByText('Nothing is running. A due run starts once a node picks it up.')).toBeTruthy()
  })

  it('shows a run another cached list holds once it starts, and drops it and its count once it finishes', async () => {
    const queued = run('Queued', { function: 'export.ledger' })
    const running = run('InProgress', { function: 'sandbox.progress' })
    const runs = [queued, running]
    const view = open({ runs }, { path: 'runs' })

    // The All runs list now holds the queued run, so its change patches that list rather than arriving as new.
    await screen.findByRole('row', { name: /export\.ledger/ })
    await act(() => view.navigate(''))
    const card = await screen.findByRole('region', { name: 'Processing now' })
    await within(card).findByText('sandbox.progress')
    expect(within(card).getByText('1 running')).toBeTruthy()

    runs[0] = { ...queued, status: 'InProgress', version: '2' }
    view.stream.push('change', change(runs[0]))

    expect(await within(card).findByText('export.ledger')).toBeTruthy()
    await waitFor(() => expect(within(card).getByText('2 running')).toBeTruthy())

    runs[1] = { ...running, status: 'Succeeded', version: '2' }
    view.stream.push('change', change(runs[1]))

    await waitFor(() => expect(within(card).queryByText('sandbox.progress')).toBeNull())
    expect(within(card).getByText('1 running')).toBeTruthy()
  })

  it('cancels the in-progress run its Cancel sits beside, after the confirm', async () => {
    const actor = user()
    const first = run('InProgress', { function: 'a.first' })
    const second = run('InProgress', { function: 'a.second' })
    const view = open({
      runs: [first, second],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.cancel'
          ? { rows: [{ id: second.id, outcome: 'done' }] }
          : undefined,
    })

    const card = await screen.findByRole('region', { name: 'Processing now' })
    const row = (await within(card).findByRole('link', { name: 'a.second' })).closest('li')!
    const cancel = within(row).getByRole('button', { name: 'Cancel' })
    await waitFor(() => expect(cancel.getAttribute('aria-disabled')).toBeNull())
    await actor.click(cancel)
    const dialog = await screen.findByRole('dialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'stuck on a lock')
    await actor.click(within(dialog).getByRole('button', { name: 'Cancel run' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.cancel')?.body).toMatchObject({
        ids: [second.id],
      }),
    )
  })

  it('disables a Processing now Cancel the server refuses, with its reason', async () => {
    const stuck = run('InProgress', { function: 'a.stuck' })
    open({
      runs: [stuck],
      extra: (request) =>
        request.path === 'jobs/commands/allowed'
          ? {
              objects: [
                {
                  kind: 'run',
                  id: stuck.id,
                  commands: [{ id: 'jobs.cancel', allowed: false, disabledReason: 'Already stopping' }],
                },
              ],
            }
          : undefined,
    })

    const card = await screen.findByRole('region', { name: 'Processing now' })
    await within(card).findByText('a.stuck')
    const cancel = within(card).getByRole('button', { name: 'Cancel' })
    await waitFor(() => expect(cancel.getAttribute('aria-disabled')).toBe('true'))
    expect(described(cancel)).toBe('Already stopping')
  })

  it('refreshes every section from Refresh while live updates are stopped', async () => {
    const view = open({ runs: [run('InProgress')], cronJobs: [cronJob()], counts: { InProgress: 1 }, figures })
    const paths = ['counts', 'overview', 'needs-you', 'jobs/runs', 'jobs/cron-jobs']
    const reads = () => paths.map((path) => view.requests.filter((request) => request.path === path).length)

    await waitFor(() => expect(reads().every((count) => count > 0)).toBe(true))
    act(() => view.stream.push('module.stale', { module: 'jobs', stale: true }))
    const before = reads()
    await user().click(await screen.findByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(reads().map((count, index) => count > before[index]!)).toEqual(paths.map(() => true)))
  })

  it('lists the next recurring runs soonest first, leaving out paused ones', async () => {
    open({
      cronJobs: [
        cronJob({ id: 'c1', function: 'reports.daily', expression: '0 6 * * *', nextDueUtc: '2026-11-06T06:00:00Z' }),
        cronJob({
          id: 'c2',
          function: 'sandbox.progress',
          expression: '*/30 * * * * *',
          nextDueUtc: '2026-11-05T10:00:06Z',
        }),
        cronJob({ id: 'c3', function: 'cleanup.sessions', isPaused: true, nextDueUtc: '2026-11-05T10:00:01Z' }),
      ],
    })

    const card = await screen.findByRole('region', { name: 'Next recurring runs' })
    await waitFor(() =>
      expect(
        within(card)
          .getAllByRole('listitem')
          .map((item) => item.querySelector('a')?.textContent),
      ).toEqual(['sandbox.progress', 'reports.daily']),
    )
    expect(within(card).getByRole('link', { name: 'All 3' }).getAttribute('href')).toBe('/jobs/cron-jobs')
    expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' }).closest('header')?.textContent).toContain(
      'Next execution in 6 seconds · sandbox.progress',
    )
  })

  it('says there are no recurring jobs yet and offers Add recurring job', async () => {
    const actor = user()
    const view = open({ cronJobs: [] })

    const card = await screen.findByRole('region', { name: 'Next recurring runs' })
    await within(card).findByText(/No recurring jobs yet/)
    await actor.click(within(card).getByRole('button', { name: 'Add recurring job' }))

    await waitFor(() =>
      expect(`${view.location().pathname}${view.location().search}`).toBe('/jobs/cron-jobs?create=cron-job'),
    )
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
          recommended: {
            command: {},
            filter: { functions: ['billing.charge'], fingerprint: 'v1:card', actionableOnly: true },
          },
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
    expect(
      (await within(card).findByRole('link', { name: '3 failed runs of billing.charge' })).getAttribute('href'),
    ).toBe('/jobs/runs?status=Failed&function=billing.charge&fingerprint=v1%3Acard')
    expect(within(card).getByRole('link', { name: 'No live node runs mail.send' }).getAttribute('href')).toBe(
      '/jobs/functions/mail.send',
    )
  })

  it('opens the add form on the runs list from Add time job', async () => {
    const view = open({})

    await user().click(await screen.findByRole('button', { name: 'Add time job' }))

    expect(await screen.findByRole('region', { name: 'Add time job' })).toBeTruthy()
    expect(view.location().pathname).toBe('/jobs/runs')
  })

  it('shows the scheduled time job as the next execution when it is due before the next cron fire', async () => {
    const view = open({
      cronJobs: [cronJob({ function: 'reports.nightly', nextDueUtc: '2026-11-06T01:00:00Z' })],
      extra: (request) =>
        request.path === 'jobs/runs' && request.query?.kind === 'TimeJob'
          ? {
              items: [run('Idle', { function: 'export.ledger', executionTime: '2026-11-05T12:00:00Z' })],
              count: 1,
              countCapped: false,
            }
          : undefined,
    })

    const header = (await screen.findByRole('heading', { level: 1, name: 'Dashboard' })).closest('header')!
    await waitFor(() => expect(header.textContent).toContain('Next execution in 2 hours · export.ledger'))
    expect(view.requests.find((request) => request.query?.kind === 'TimeJob')?.query).toMatchObject({
      status: ['Idle', 'Queued'],
      from: expect.stringMatching(/^2026-11-05T10:00:/),
      sort: 'executionTime',
      direction: 'ascending',
      limit: '1',
    })
  })

  it('keeps the next cron fire as the next execution when no time job is due before it', async () => {
    open({
      cronJobs: [cronJob({ function: 'reports.nightly', nextDueUtc: '2026-11-05T11:00:00Z' })],
      extra: (request) =>
        request.path === 'jobs/runs' && request.query?.kind === 'TimeJob'
          ? {
              items: [run('Idle', { function: 'export.ledger', executionTime: '2026-11-05T12:00:00Z' })],
              count: 1,
              countCapped: false,
            }
          : undefined,
    })

    const header = (await screen.findByRole('heading', { level: 1, name: 'Dashboard' })).closest('header')!
    await waitFor(() => expect(header.textContent).toContain('Next execution in 60 minutes · reports.nightly'))
  })
})

const everyStatus = {
  Idle: 37,
  Queued: 12,
  InProgress: 4,
  Succeeded: 4_210,
  DueDone: 0,
  Failed: 23,
  Cancelled: 1,
  Skipped: 0,
}

/** The sidebar counts as the server answers them, with the named statuses capped. */
function counted(counts: Readonly<Record<string, number>>, capped: readonly string[] = []) {
  return (request: { path: string }) =>
    request.path === 'counts'
      ? {
          counts: Object.fromEntries(
            Object.entries(counts).map(([key, count]) => [key, { count, capped: capped.includes(key) }]),
          ),
          stale: [],
        }
      : undefined
}

describe('jobs dashboard status distribution', () => {
  it('shows capped counts alone, with no shares and no donut', async () => {
    open({ extra: counted({ ...everyStatus, Failed: 10_000 }, ['Failed']) })

    const card = await screen.findByRole('region', { name: 'Runs by status' })
    const table = await within(card).findByRole('table', { name: 'Runs by status' })
    const rows = within(table)
      .getAllByRole('row')
      .map((row) => row.textContent)
    expect(rows).toContain('Failed10,000+ runs')
    expect(rows).toContain('Succeeded4,210 runs')
    expect(table.textContent).not.toContain('%')
    expect(within(table).queryByRole('columnheader', { name: 'Share' })).toBeNull()
    expect(within(card).queryByRole('img')).toBeNull()
    expect(within(card).queryByRole('button', { name: 'Show chart' })).toBeNull()
    expect(within(card).getByText('Counts of all runs. These counts are partial, so no shares are shown.')).toBeTruthy()
  })

  it('shows exact counts as a donut with shares, switchable to the list', async () => {
    const actor = user()
    open({ extra: counted(everyStatus) })

    const card = await screen.findByRole('region', { name: 'Runs by status' })
    const donut = await within(card).findByRole('img', { name: /^Runs by status:/ })
    expect(donut.getAttribute('aria-label')).toContain('Failed 23 runs, 0.5%')

    await actor.click(within(card).getByRole('button', { name: 'Show list' }))
    const table = within(card).getByRole('table', { name: 'Runs by status' })
    expect(
      within(table)
        .getAllByRole('row')
        .map((row) => row.textContent),
    ).toContain('Succeeded4,210 runs98.2%')
    expect(within(card).queryByRole('img')).toBeNull()
  })
})

describe('jobs dashboard runs per day', () => {
  it('asks for the last 7 days and the next 7 in the display zone, and draws an unread day as a gap', async () => {
    const actor = user()
    localStorage.setItem('headless-dashboard:time-zone', 'Africa/Cairo')
    const view = open({ daily: (date) => (date === '2026-11-03' ? null : { Succeeded: 3 }) })

    const card = await screen.findByRole('region', { name: 'Runs per day' })
    const chart = await within(card).findByRole('img', { name: /^Runs per day by status/ })
    expect(view.requests.find((request) => request.path === 'jobs/series/daily')?.query).toEqual({
      from: '2026-10-30',
      to: '2026-11-12',
      zone: 'Africa/Cairo',
    })
    expect(chart.getAttribute('aria-label')).toContain(
      'unmeasured buckets are gaps; Today is Nov 5, later buckets are scheduled and drawn outlined',
    )

    await actor.click(within(card).getByRole('button', { name: 'Show list' }))
    const rows = within(within(card).getByRole('table', { name: 'Runs per day by status' })).getAllByRole('row')
    const unread = rows.find((row) => row.textContent?.startsWith('Nov 3'))!
    expect(unread.textContent).toContain('not measured')
    expect(unread.textContent).not.toContain('0 runs')
    expect(rows.find((row) => row.textContent?.startsWith('Nov 4'))?.textContent).toContain('3 runs')
    // Today is Nov 5 in Cairo; the days after it count scheduled runs.
    expect(rows.find((row) => row.textContent?.startsWith('Nov 5'))?.textContent).toContain('Nov 5 · Today')
    expect(rows.find((row) => row.textContent?.startsWith('Nov 12'))?.textContent).toContain('Nov 12 · scheduled')
  })

  it('asks for the chosen preset’s days', async () => {
    const actor = user()
    const view = open({})

    const card = await screen.findByRole('region', { name: 'Runs per day' })
    await within(card).findByRole('img', { name: /^Runs per day by status/ })
    await actor.click(within(card).getByRole('button', { name: 'Last 30 days' }))

    await waitFor(() =>
      expect(view.requests.filter((request) => request.path === 'jobs/series/daily').at(-1)?.query).toEqual({
        from: '2026-10-07',
        to: '2026-11-05',
        zone: 'UTC',
      }),
    )
  })
})
