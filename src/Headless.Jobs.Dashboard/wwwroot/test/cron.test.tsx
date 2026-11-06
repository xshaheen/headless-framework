import { screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cronJob, now, open, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
})

const fires = ['2026-11-06T01:00:00Z', '2026-11-07T01:00:00Z', '2026-11-08T01:00:00Z', '2026-11-09T01:00:00Z', '2026-11-10T01:00:00Z']

function described(button: HTMLElement) {
  return document.getElementById(button.getAttribute('aria-describedby')!)?.textContent
}

describe('jobs cron job page', () => {
  it('lists five next fires in the scheduler zone', async () => {
    const job = cronJob({ nextFires: fires })
    open({ cronJobs: [job] }, { path: `cron-jobs/${job.id}` })

    const list = await screen.findByRole('list', { name: 'Next fires, Europe/Berlin' })
    expect(within(list).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'Nov 6, 2026, 02:00:00 GMT+01:00 Europe/Berlin',
      'Nov 7, 2026, 02:00:00 GMT+01:00 Europe/Berlin',
      'Nov 8, 2026, 02:00:00 GMT+01:00 Europe/Berlin',
      'Nov 9, 2026, 02:00:00 GMT+01:00 Europe/Berlin',
      'Nov 10, 2026, 02:00:00 GMT+01:00 Europe/Berlin',
    ])
  })

  it('disables Run now on a paused cron job with the server’s reason, and lists no fires', async () => {
    const job = cronJob({ isPaused: true })
    open({ cronJobs: [job] }, { path: `cron-jobs/${job.id}` })

    const runNow = await screen.findByRole('button', { name: 'Run now' })
    await waitFor(() => expect(described(runNow)).toBe('Paused — resume first'))
    expect(runNow.getAttribute('aria-disabled')).toBe('true')
    expect(screen.getByText('No fires scheduled. Resume the cron job to schedule its next fires.')).toBeTruthy()
  })

  it('shows a typed expression’s reading and next fires without committing it', async () => {
    const actor = user()
    const job = cronJob({ nextFires: fires })
    const view = open(
      {
        cronJobs: [job],
        extra: (request) =>
          request.path === 'jobs/cron-preview' && request.query?.expression === '*/5 * * * *'
            ? { timeZoneId: 'Europe/Berlin', reading: 'Every 5 minutes', nextFires: ['2026-11-05T10:05:00Z', '2026-11-05T10:10:00Z'] }
            : undefined,
      },
      { path: `cron-jobs/${job.id}` },
    )

    const field = await screen.findByRole('textbox', { name: 'Expression' })
    await actor.clear(field)
    await actor.type(field, '*/5 * * * *')

    expect(await screen.findByText('Every 5 minutes')).toBeTruthy()
    expect(screen.getByText('Nov 5, 2026, 11:05:00 GMT+01:00 Europe/Berlin')).toBeTruthy()
    expect(view.requests.some((request) => request.method === 'POST')).toBe(false)
  })

  it('shows the server’s message for an invalid expression and does not commit it', async () => {
    const actor = user()
    const job = cronJob()
    const view = open(
      {
        cronJobs: [job],
        extra: (request) =>
          request.path === 'jobs/cron-preview' && request.query?.expression === '61 * * * *'
            ? { timeZoneId: 'Europe/Berlin', nextFires: [], validationMessage: 'Minute 61 is out of range (0-59).' }
            : undefined,
      },
      { path: `cron-jobs/${job.id}` },
    )

    const field = await screen.findByRole('textbox', { name: 'Expression' })
    await actor.clear(field)
    await actor.type(field, '61 * * * *')
    expect(await screen.findByText('Minute 61 is out of range (0-59).')).toBeTruthy()

    await actor.keyboard('{Enter}')

    expect((await screen.findByRole('alert')).textContent).toBe('Minute 61 is out of range (0-59).')
    expect(view.requests.some((request) => request.method === 'POST')).toBe(false)
  })

  it('commits a valid expression through Edit schedule on Enter', async () => {
    const actor = user()
    const job = cronJob()
    const view = open(
      {
        cronJobs: [job],
        extra: (request) =>
          request.method === 'POST' && request.path === 'jobs/commands/jobs.edit-cron' ? { rows: [{ id: job.id, outcome: 'done' }] } : undefined,
      },
      { path: `cron-jobs/${job.id}` },
    )

    const field = await screen.findByRole('textbox', { name: 'Expression' })
    await actor.clear(field)
    await actor.type(field, '0 3 * * *{Enter}')

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.edit-cron')?.body).toMatchObject({
        ids: [job.id],
        arguments: { expression: '0 3 * * *' },
      }),
    )
  })

  it('locks the expression and zone of a cron job defined in code', async () => {
    const job = cronJob({ definedInCode: true })
    open({ cronJobs: [job] }, { path: `cron-jobs/${job.id}` })

    expect(await screen.findByText('Defined in code')).toBeTruthy()
    expect(screen.queryByRole('textbox', { name: 'Expression' })).toBeNull()
    expect(screen.queryByRole('textbox', { name: 'Time zone' })).toBeNull()
  })
})

describe('jobs cron jobs list', () => {
  it('says how cron jobs get there when there are none', async () => {
    open({}, { path: 'cron-jobs' })

    expect(await screen.findByText('No cron jobs. Declare one with a [Job] cron attribute, or add one here.')).toBeTruthy()
  })

  it('does not carry an expression typed for one cron job into the next one selected', async () => {
    const actor = user()
    const first = cronJob({ description: 'Nightly report' })
    const second = cronJob({ id: 'c0000000-0000-0000-0000-000000000002', description: 'Nightly cleanup' })
    const view = open(
      {
        cronJobs: [first, second],
        extra: (request) =>
          request.path === 'jobs/cron-preview' && request.query?.expression === '61 * * * *'
            ? { timeZoneId: 'Europe/Berlin', nextFires: [], validationMessage: 'Minute 61 is out of range (0-59).' }
            : undefined,
      },
      { path: 'cron-jobs' },
    )

    await actor.click(await screen.findByRole('row', { name: /Nightly report/ }))
    const field = await screen.findByRole('textbox', { name: 'Expression' })
    await actor.clear(field)
    await actor.type(field, '61 * * * *')
    await actor.click(screen.getByRole('row', { name: /Nightly cleanup/ }))

    await screen.findByRole('complementary', { name: `Cron job ${second.id}` })
    expect((screen.getByRole('textbox', { name: 'Expression' }) as HTMLInputElement).value).toBe('0 2 * * *')
    expect(view.requests.some((request) => request.method === 'POST')).toBe(false)
  })
})
