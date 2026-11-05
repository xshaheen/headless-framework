import { act, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import jobs from '../src/module.ts'
import { change, now, open, run, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
})

function described(button: HTMLElement) {
  return document.getElementById(button.getAttribute('aria-describedby')!)?.textContent
}

describe('jobs runs list', () => {
  it('shows each status as mark and label, and patches a row in place from a live change', async () => {
    const queued = run('Queued', { function: 'a.queued' })
    const view = open({
      runs: [
        run('Failed', { function: 'a.failed' }),
        run('InProgress', { function: 'a.running' }),
        run('DueDone', { function: 'a.late' }),
        run('Idle', { function: 'a.idle' }),
        run('Skipped', { function: 'a.skipped' }),
        queued,
      ],
    })

    const expected: [string, string, string][] = [
      ['a.failed', 'Failed', 'hd-tone-needs'],
      ['a.running', 'In progress', 'hd-tone-active'],
      ['a.late', 'Succeeded late', 'hd-tone-healthy'],
      ['a.idle', 'Idle', 'hd-tone-waiting'],
      ['a.skipped', 'Skipped', 'hd-tone-stopped'],
    ]

    for (const [name, label, tone] of expected) {
      const mark = within(await screen.findByRole('row', { name: new RegExp(name) })).getByText(label)
      expect(mark.className).toContain(tone)
    }

    expect(view.stream.topics()).toContain('jobs.run')
    const before = view.requests.filter((request) => request.path === 'jobs/runs').length

    view.stream.push('change', change({ ...queued, status: 'InProgress', version: '2' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.queued/ })).getByText('In progress')).toBeTruthy())
    expect(view.requests.filter((request) => request.path === 'jobs/runs')).toHaveLength(before)
  })

  it('says no runs match with Clear filters, and clearing shows every run', async () => {
    const actor = user()
    open({ runs: [run('Succeeded', { function: 'a.done' })] }, { path: '?status=Failed' })

    expect(await screen.findByText(/No runs match\. Clear filters or widen the time range\./)).toBeTruthy()

    await actor.click(screen.getByRole('button', { name: 'Clear filters' }))

    expect(await screen.findByRole('row', { name: /a\.done/ })).toBeTruthy()
  })

  it('shows a failed load with Retry, never an empty list', async () => {
    let fail = true
    open({
      runs: [run('Queued', { function: 'a.queued' })],
      extra: (request) => {
        if (request.path === 'jobs/runs' && fail) {
          throw new DashboardApiError(503, 'The Jobs store did not answer. Try again.')
        }

        return undefined
      },
    })

    expect((await screen.findByRole('alert')).textContent).toBe('The Jobs store did not answer. Try again.')
    expect(screen.queryByText(/No runs/)).toBeNull()

    fail = false
    await user().click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('row', { name: /a\.queued/ })).toBeTruthy()
  })

  it('shows the loading skeleton only after 150 ms', async () => {
    open({ extra: (request) => (request.path === 'jobs/runs' ? new Promise(() => undefined) : undefined) })
    await screen.findByRole('search', { name: 'Filter runs' })

    await act(() => vi.advanceTimersByTimeAsync(100))
    expect(screen.queryByRole('status', { name: 'Loading runs' })).toBeNull()

    await act(() => vi.advanceTimersByTimeAsync(60))
    expect(screen.getByRole('status', { name: 'Loading runs' })).toBeTruthy()
  })

  it('shows the selected run’s attempts in order, each with its node and exception', async () => {
    const failed = run('Failed', { function: 'a.failed' })
    open({
      runs: [failed],
      attempts: {
        [failed.id]: [
          { number: 1, node: 'node-a', startedAt: '2026-11-05T09:51:00Z', endedAt: '2026-11-05T09:52:00Z', outcome: 'Lost' },
          {
            number: 2,
            node: 'node-b',
            startedAt: '2026-11-05T09:55:00Z',
            endedAt: '2026-11-05T09:56:00Z',
            outcome: 'Failed',
            exceptionMessage: 'Gateway timeout\n   at Billing.Charge()',
          },
        ],
      },
    })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))

    const inspector = screen.getByRole('complementary', { name: `Run ${failed.id}` })
    const timeline = await within(inspector).findByRole('grid', { name: 'Run timeline' })
    await waitFor(() => expect(within(timeline).getAllByRole('row')).toHaveLength(3))
    expect(within(timeline).getAllByRole('row').map((row) => row.textContent)).toEqual([
      'Created 10 minutes ago',
      'Attempt 1: Lost 8 minutes ago by node-a',
      'Attempt 2: Failed 4 minutes ago by node-b Gateway timeout',
    ])
  })

  it('reads Cancelling… on a run with a pending cancel and keeps its stored status until the stream changes it', async () => {
    const running = run('InProgress', { function: 'a.running', cancelRequested: true })
    const view = open({ runs: [running] })

    const row = await screen.findByRole('row', { name: /a\.running/ })
    expect(within(row).getByText('Cancelling…').className).toContain('hd-tone-active')

    await user().click(row)
    const inspector = screen.getByRole('complementary', { name: `Run ${running.id}` })
    expect(within(inspector).getByText('In progress, cancel requested')).toBeTruthy()

    view.stream.push('change', change({ ...running, status: 'Cancelled', version: '2' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.running/ })).getByText('Cancelled')).toBeTruthy())
    expect(screen.queryByText('Cancelling…')).toBeNull()
  })

  it('reads Requeuing… after a requeue until the stream sets the real status', async () => {
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    const view = open({
      runs: [failed],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.requeue' ? { rows: [{ id: failed.id, outcome: 'done' }] } : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /a\.failed/ }))
    const inspector = screen.getByRole('complementary', { name: `Run ${failed.id}` })
    const requeue = await within(inspector).findByRole('button', { name: 'Requeue' })
    await waitFor(() => expect(requeue.getAttribute('aria-disabled')).toBeNull())
    await actor.click(requeue)

    const dialog = await screen.findByRole('dialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: 'Requeue' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.failed/ })).getByText('Requeuing…')).toBeTruthy())

    view.stream.push('change', change({ ...failed, status: 'Queued', version: '2' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.failed/ })).getByText('Queued')).toBeTruthy())
  })

  it('shows the server’s reason on an action it refuses', async () => {
    open({ runs: [run('InProgress', { function: 'a.running' })] })

    await user().click(await screen.findByRole('row', { name: /a\.running/ }))
    const requeue = await within(screen.getByRole('complementary', { name: /^Run / })).findByRole('button', { name: 'Requeue' })

    await waitFor(() => expect(described(requeue)).toBe('Running — cancel instead'))
    expect(requeue.getAttribute('aria-disabled')).toBe('true')
  })

  it('disables every operate action for a viewer, with Needs operate permission', async () => {
    open({ runs: [run('Failed', { function: 'a.failed' })] }, { permissions: { canOperate: false } })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))

    for (const name of ['Add time job', 'Add chain']) {
      const button = screen.getByRole('button', { name })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(described(button)).toBe('Needs operate permission')
    }

    const inspector = screen.getByRole('complementary', { name: /^Run / })

    for (const name of ['Requeue', 'Cancel', 'Delete']) {
      const button = await within(inspector).findByRole('button', { name })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(described(button)).toBe('Needs operate permission')
    }
  })

  it('starts an Operation confirm with the exact count when every matching failed run is selected', async () => {
    const actor = user()
    const failed = [run('Failed', { function: 'billing.charge' }), run('Failed', { function: 'billing.charge' })]
    const view = open(
      {
        runs: failed,
        extra: (request) => {
          if (request.path === 'jobs/runs') {
            // The first page of 312 matching runs; the page after it is not needed here.
            return request.query?.after
              ? { items: [], count: 312, countCapped: false, nextCursor: null }
              : { items: failed, count: 312, countCapped: false, nextCursor: 'next' }
          }

          if (request.path === 'jobs/commands/jobs.requeue/confirm') {
            return { operationId: 'op1', count: 312, confirmation: 'typed', sample: [], token: 't', expiresAt: '', chunkSize: 500 }
          }

          return undefined
        },
      },
      { path: '?status=Failed&function=billing.charge' },
    )

    const rows = await screen.findAllByRole('row', { name: /billing\.charge/ })
    await actor.click(rows[0]!)
    await actor.keyboard('{Shift>}')
    await actor.click(rows[1]!)
    await actor.keyboard('{/Shift}')
    await actor.click(await screen.findByRole('button', { name: 'Select all 312 matching' }))

    const inspector = await screen.findByRole('complementary', { name: 'All 312 matching failed runs of billing.charge' })
    await actor.click(within(inspector).getByRole('button', { name: 'Requeue 312' }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))

    expect(await screen.findByRole('button', { name: 'Requeue 312 failed runs of billing.charge' })).toBeTruthy()
    expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
      filter: { statuses: ['Failed'], functions: ['billing.charge'] },
      reason: 'gateway fixed',
    })
  })

  it('opens the runs list filtered by the run’s correlation id on T', async () => {
    const actor = user()
    const first = run('Succeeded', { function: 'orders.place', correlationId: 'corr-7f2' })
    const related = run('Failed', { function: 'billing.charge', correlationId: 'corr-7f2' })
    const other = run('Succeeded', { function: 'mail.send', correlationId: 'corr-other' })
    const view = open({ runs: [first, related, other] })

    await actor.click(await screen.findByRole('row', { name: /orders\.place/ }))
    await actor.keyboard('t')

    await waitFor(() => expect(screen.queryByRole('row', { name: /mail\.send/ })).toBeNull())
    expect(screen.getByRole('row', { name: /billing\.charge/ })).toBeTruthy()
    expect(view.requests.at(-1)?.query?.correlationId ?? view.requests.find((request) => request.query?.correlationId)?.query?.correlationId).toBe(
      'corr-7f2',
    )
    expect(screen.getByText('corr-7f2', { selector: 'b' })).toBeTruthy()
  })

  it('creates a chain with a failure branch, submits the tree, and selects the new chain', async () => {
    const actor = user()
    const created = run('Idle', { id: 'a0000000-0000-0000-0000-00000000000c', function: 'orders.place' })
    const rows = [run('Succeeded', { function: 'a.done' })]
    const view = open({
      runs: rows,
      extra: (request) => {
        if (request.method === 'POST' && request.path === 'jobs/time-jobs') {
          rows.push(created)

          return { id: created.id }
        }

        return undefined
      },
    })

    await screen.findByRole('row', { name: /a\.done/ })
    await actor.click(screen.getByRole('button', { name: 'Add chain' }))

    const form = await screen.findByRole('complementary', { name: 'Add chain' })
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[0]!, 'billing.charge')
    await actor.type(within(form).getAllByRole('textbox', { name: /Arguments/ })[0]!, '{{"invoiceId": 42}')
    await actor.click(within(form).getByRole('button', { name: 'Add step after this' }))
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[1]!, 'billing.refund')
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Runs' }), 'On failure')
    await actor.click(within(form).getByRole('button', { name: 'Add chain' }))

    expect(await screen.findByRole('complementary', { name: `Run ${created.id}` })).toBeTruthy()
    expect(view.requests.find((request) => request.path === 'jobs/time-jobs')?.body).toEqual({
      function: 'billing.charge',
      request: { invoiceId: 42 },
      children: [{ function: 'billing.refund', runCondition: 'OnFailure' }],
      retries: 0,
    })
  })

  it('keeps the form open with the server’s message when it refuses the submit', async () => {
    const actor = user()
    open({
      extra: (request) => {
        if (request.method === 'POST' && request.path === 'jobs/time-jobs') {
          throw new DashboardApiError(400, "The request does not match billing.charge's argument type.")
        }

        return undefined
      },
    })

    await actor.click(await screen.findByRole('button', { name: 'Add time job' }))
    const form = await screen.findByRole('complementary', { name: 'Add time job' })
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Function' }), 'billing.charge')
    await actor.click(within(form).getByRole('button', { name: 'Add time job' }))

    expect((await within(form).findByRole('alert')).textContent).toBe("The request does not match billing.charge's argument type.")
    expect(screen.getByRole('complementary', { name: 'Add time job' })).toBeTruthy()
  })

  it('opens the Requeue Operation over the failed runs from the palette command', async () => {
    let target = ''
    jobs.commands.find((command) => command.id === 'jobs.requeue-failed')!.run({ navigate: (to) => (target = to) })

    const view = open({ runs: [run('Failed', { function: 'a.failed' })] })
    await screen.findByRole('row', { name: /a\.failed/ })
    await act(() => view.navigate(target))

    expect(await screen.findByRole('button', { name: 'Count rows' })).toBeTruthy()
  })
})

describe('jobs run page', () => {
  it('says the run no longer exists on 404', async () => {
    open({}, { path: 'runs/00000000-0000-0000-0000-00000000dead' })

    expect((await screen.findByRole('alert')).textContent).toBe('This run no longer exists.')
    expect(screen.getByRole('link', { name: 'Back to runs' })).toBeTruthy()
  })

  it('opens a selected attempt in the inspector with its node and exception', async () => {
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    open(
      {
        runs: [failed],
        attempts: {
          [failed.id]: [{ number: 1, node: 'node-b', startedAt: '2026-11-05T09:55:00Z', outcome: 'Failed', exceptionMessage: 'Gateway timeout' }],
        },
      },
      { path: `runs/${failed.id}` },
    )

    const timeline = await screen.findByRole('grid', { name: 'Run timeline' })
    await waitFor(() => expect(within(timeline).getAllByRole('row')).toHaveLength(2))
    await actor.click(within(timeline).getAllByRole('row')[1]!)

    const inspector = await screen.findByRole('complementary', { name: 'Attempt 1' })
    expect(within(inspector).getByText('node-b')).toBeTruthy()
    expect(within(inspector).getByText('Gateway timeout')).toBeTruthy()
  })
})
