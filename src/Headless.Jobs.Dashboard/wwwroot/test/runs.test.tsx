import { act, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import type { RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import jobs from '../src/module.ts'
import { change, now, open, run, user, type FakeServer } from './fake-server.ts'

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

/** The runs list, at `runs` and the query given. */
function list(fake: FakeServer, query = '', options: RenderModuleOptions = {}) {
  return open(fake, { path: `runs${query}`, ...options })
}

/** The open run's panel. */
function panel(id: string) {
  return screen.getByRole('region', { name: `Run ${id}` })
}

function field(scope: HTMLElement, label: string) {
  return within(scope).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent
}

describe('jobs runs list', () => {
  it('shows each status as mark and label, and patches a row in place from a live change', async () => {
    const queued = run('Queued', { function: 'a.queued' })
    const view = list({
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
      ['a.failed', 'Failed', 'hd-tone-fail'],
      ['a.running', 'In progress', 'hd-tone-proc'],
      ['a.late', 'Succeeded late', 'hd-tone-ok'],
      ['a.idle', 'Idle', 'hd-tone-sched'],
      ['a.skipped', 'Skipped', 'hd-tone-off'],
    ]

    for (const [name, label, tone] of expected) {
      const mark = within(await screen.findByRole('row', { name: new RegExp(name) })).getByText(label)
      expect(mark.className).toContain(tone)
    }

    expect(view.stream.topics()).toContain('jobs.run')
    const before = view.requests.filter((request) => request.path === 'jobs/runs').length

    view.stream.push('change', change({ ...queued, status: 'InProgress', version: '2' }))

    await waitFor(() =>
      expect(within(screen.getByRole('row', { name: /a\.queued/ })).getByText('In progress')).toBeTruthy(),
    )
    expect(view.requests.filter((request) => request.path === 'jobs/runs')).toHaveLength(before)
  })

  it('heads a state list with its title, served count, and what the state holds', async () => {
    list({ runs: [run('Idle'), run('Idle'), run('Failed')], counts: { Idle: 2 } }, '?status=Idle')

    const title = await screen.findByRole('heading', { level: 1, name: 'Idle' })
    const header = title.closest('header')!
    await waitFor(() => expect(within(header).getByText('2')).toBeTruthy())
    expect(
      within(header).getByText('Due later, waiting for a retry, or a chain step waiting for its parent.'),
    ).toBeTruthy()
    expect(screen.getAllByRole('row', { name: /billing\.charge/ })).toHaveLength(2)
  })

  it('reads each unset filter as Any, not None', async () => {
    list({ runs: [run('Failed')] })

    await screen.findByRole('row', { name: /billing\.charge/ })
    const filters = within(screen.getByRole('group', { name: 'Filters' }))
    const unset = ['Kind', 'Function', 'Due'].map(
      (name) => filters.getByRole('button', { name: new RegExp(name) }).textContent,
    )
    expect(unset).toEqual(['Any kind', 'Any function', 'Due'])
  })

  it('counts only arrived runs that match the filter in N new', async () => {
    const view = list({ runs: [run('Failed', { function: 'a.kept' })] }, '?function=a.kept')
    await screen.findByRole('row', { name: /a\.kept/ })

    view.stream.push('change', change(run('Failed', { function: 'b.other' })))
    view.stream.push('change', change(run('Failed', { function: 'b.other' })))
    view.stream.push('change', change(run('Queued', { function: 'a.kept' })))

    expect(await screen.findByRole('button', { name: '1 new' })).toBeTruthy()
  })

  it('says no runs match with Clear filters, and clearing keeps the state but drops the rest', async () => {
    const actor = user()
    list({ runs: [run('Failed', { function: 'a.failed' })] }, '?status=Failed&function=a.none')

    expect(await screen.findByText('No runs match')).toBeTruthy()
    expect(screen.getByText('Clear the filters or widen the time range.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Clear function filter' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Kind/ })).toBeNull()

    await actor.click(screen.getByRole('button', { name: 'Clear filters' }))

    expect(await screen.findByRole('row', { name: /a\.failed/ })).toBeTruthy()
    expect(screen.getByRole('heading', { level: 1, name: 'Failed' })).toBeTruthy()
  })

  it('says a state list with no runs is empty in its own words', async () => {
    list({ runs: [run('Succeeded')] }, '?status=Cancelled')

    expect(await screen.findByText('No cancelled runs')).toBeTruthy()
  })

  it('shows a failed load with Retry, never an empty list', async () => {
    let fail = true
    list({
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
    list({ extra: (request) => (request.path === 'jobs/runs' ? new Promise(() => undefined) : undefined) })
    await screen.findByRole('heading', { level: 1, name: 'All runs' })

    await act(() => vi.advanceTimersByTimeAsync(100))
    expect(screen.queryByRole('status', { name: 'Loading runs' })).toBeNull()

    await act(() => vi.advanceTimersByTimeAsync(60))
    expect(screen.getByRole('status', { name: 'Loading runs' })).toBeTruthy()
  })

  it('shows a long function name in full as the hover title', async () => {
    const long = run('Failed', { function: 'sandbox.chain-step-charge-card-and-notify' })
    list({ runs: [long] })

    const row = await screen.findByRole('row', { name: /sandbox\.chain-step-charge-card-and-notify/ })
    const name = within(row).getByText('sandbox.chain-step-charge-card-and-notify')
    expect(name.closest('[title]')?.getAttribute('title')).toBe('sandbox.chain-step-charge-card-and-notify')
  })

  it('counts a run’s attempts out of those it may make, as its panel does, and leaves a run not picked up yet blank', async () => {
    const failed = run('Failed', { function: 'a.failed', retryCount: 1, retries: 3 })
    list({ runs: [failed, run('Queued', { function: 'a.queued', retries: 3 })] })

    const row = await screen.findByRole('row', { name: /a\.failed/ })
    expect(screen.getByRole('columnheader', { name: 'Attempts' })).toBeTruthy()
    expect(screen.queryByRole('columnheader', { name: 'Retries' })).toBeNull()
    expect(within(row).getByText('2 of 4')).toBeTruthy()
    expect(within(screen.getByRole('row', { name: /a\.queued/ })).queryByText(/\d of \d/)).toBeNull()

    await user().click(row)
    await user().click(within(panel(failed.id)).getByRole('tab', { name: 'Overview' }))
    expect(field(panel(failed.id), 'Attempt')).toBe('Attempt 2 of 4')
  })

  it('opens a run in the docked panel, in the URL as open, with its exception and its attempts newest first', async () => {
    const failed = run('Failed', {
      function: 'a.failed',
      exceptionMessage: 'Gateway timeout',
      exceptionStack: 'at Billing.Charge()',
    })
    const view = list({
      runs: [failed],
      attempts: {
        [failed.id]: [
          {
            number: 1,
            node: 'node-a',
            startedAt: '2026-11-05T09:51:00Z',
            endedAt: '2026-11-05T09:52:00Z',
            outcome: 'Lost',
          },
          {
            number: 2,
            node: 'node-b',
            startedAt: '2026-11-05T09:55:00Z',
            endedAt: '2026-11-05T09:56:00Z',
            outcome: 'Failed',
            exceptionMessage: 'Gateway timeout\n   at Billing.Charge()',
            failureFingerprint: 'v1:9f2c41',
          },
        ],
      },
    })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))

    expect(new URLSearchParams(view.location().search).get('open')).toBe(failed.id)
    const opened = panel(failed.id)
    // A failed run opens on its exception, with the stack already open.
    expect(within(opened).getByRole('tab', { name: 'Exception', selected: true })).toBeTruthy()
    expect(within(opened).getByText('Gateway timeout').closest('pre')?.textContent).toBe(
      'Gateway timeout\n   at Billing.Charge()',
    )

    await user().click(within(opened).getByRole('tab', { name: 'Overview' }))
    expect(field(opened, 'Failure')).toBe('Gateway timeout')
    const history = await within(opened).findByRole('region', { name: 'History' })
    expect(
      within(history)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual([
      '4 min. agoFailed try 2 · node-b · Gateway timeout · fingerprint v1:9f2c41',
      '8 min. agoLost try 1 · node-a',
      '10 min. agoCreated',
    ])
  })

  it('reads a running run and a chain step without contradicting their status', async () => {
    const actor = user()
    const running = run('InProgress', { function: 'a.running', executedAt: undefined })
    const step = run('Succeeded', { function: 'a.step', executionTime: undefined, parentId: running.id })
    list({ runs: [running, step] })

    await actor.click(await screen.findByRole('row', { name: /a\.running/ }))
    expect(field(panel(running.id), 'Duration')).toBe('Running')
    expect(field(panel(running.id), 'Executed')).toBe('Running')

    await actor.click(screen.getByRole('row', { name: /a\.step/ }))
    expect(field(panel(step.id), 'Due')).toBe('After its chain parent')
  })

  it('names the effect on the cancel confirm, and a dismiss that is not Cancel', async () => {
    const actor = user()
    const running = run('InProgress', { function: 'a.running' })
    list({ runs: [running] })

    await actor.click(await screen.findByRole('row', { name: /a\.running/ }))
    await actor.click(within(panel(running.id)).getByRole('button', { name: 'Cancel' }))

    const dialog = await screen.findByRole('alertdialog', { name: 'Cancel run' })
    expect(
      within(dialog)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Back', 'Cancel run'])
  })

  it('offers the Arguments tab only to a run that stored a payload, and the Chain tab only to a chain step', async () => {
    const quiet = run('Succeeded', { function: 'a.quiet' })
    const loud = run('Succeeded', { function: 'a.loud' })
    const step = run('Succeeded', { function: 'a.step', parentId: quiet.id })
    const actor = user()
    const view = list({
      runs: [quiet, loud, step],
      extra: (request) =>
        request.path === 'payload' && request.query?.id === loud.id
          ? { content: '{"amount":12}', contentType: 'application/json' }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /a\.quiet/ }))
    // The panel reads the payload once it opens, so the tab shows only when there is one; the server says 404 here.
    await waitFor(() => expect(view.requests.some((request) => request.path === 'payload')).toBe(true))
    expect(
      within(panel(quiet.id))
        .getAllByRole('tab')
        .map((tab) => tab.textContent),
    ).toEqual(['Overview'])
    expect(screen.queryByRole('alert')).toBeNull()

    await actor.click(screen.getByRole('row', { name: /a\.loud/ }))
    await actor.click(await within(panel(loud.id)).findByRole('tab', { name: 'Arguments' }))
    expect(await screen.findByText('12')).toBeTruthy()

    await actor.click(screen.getByRole('row', { name: /a\.step/ }))
    await actor.click(await within(panel(step.id)).findByRole('tab', { name: 'Chain' }))
    expect(field(panel(step.id), 'Runs after')).toBe(quiet.id)
  })

  it('reads Cancelling… on a run with a pending cancel and keeps its stored status until the stream changes it', async () => {
    const running = run('InProgress', { function: 'a.running', cancelRequested: true })
    const view = list({ runs: [running] })

    const row = await screen.findByRole('row', { name: /a\.running/ })
    expect(within(row).getByText('Cancelling…').className).toContain('hd-tone-proc')

    await user().click(row)
    expect(field(panel(running.id), 'Status')).toBe('In progress, cancel requested')

    view.stream.push('change', change({ ...running, status: 'Cancelled', version: '2' }))

    await waitFor(() =>
      expect(within(screen.getByRole('row', { name: /a\.running/ })).getByText('Cancelled')).toBeTruthy(),
    )
    expect(screen.queryByText('Cancelling…')).toBeNull()
    expect(screen.queryByText('Not honoured: the handler kept running')).toBeNull()
  })

  it('keeps the status a run really ended with when its handler ignored the cancel, and says so in the field grid', async () => {
    const stubborn = run('Succeeded', { function: 'a.stubborn', cancelRequested: true })
    list({ runs: [stubborn] })

    const row = await screen.findByRole('row', { name: /a\.stubborn/ })
    expect(within(row).getByText('Succeeded')).toBeTruthy()

    await user().click(row)
    expect(field(panel(stubborn.id), 'Cancel request')).toBe('Not honoured: the handler kept running')
  })

  it('reads Requeuing… after a requeue until the stream sets the real status', async () => {
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    const view = list({
      runs: [failed],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.requeue'
          ? { rows: [{ id: failed.id, outcome: 'done' }] }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /a\.failed/ }))
    const requeue = await within(panel(failed.id)).findByRole('button', { name: 'Requeue' })
    await waitFor(() => expect(requeue.getAttribute('aria-disabled')).toBeNull())
    await actor.click(requeue)

    const dialog = await screen.findByRole('alertdialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: 'Requeue run' }))

    await waitFor(() =>
      expect(within(screen.getByRole('row', { name: /a\.failed/ })).getByText('Requeuing…')).toBeTruthy(),
    )

    view.stream.push('change', change({ ...failed, status: 'Queued', version: '2' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.failed/ })).getByText('Queued')).toBeTruthy())
  })

  it('shows the server’s reason on an action it refuses', async () => {
    const running = run('InProgress', { function: 'a.running' })
    list({ runs: [running] })

    await user().click(await screen.findByRole('row', { name: /a\.running/ }))
    const requeue = await within(panel(running.id)).findByRole('button', { name: 'Requeue' })

    await waitFor(() => expect(described(requeue)).toBe('Running — cancel instead'))
    expect(requeue.getAttribute('aria-disabled')).toBe('true')
  })

  it('disables every operate action for a viewer, with Needs operate permission', async () => {
    const failed = run('Failed', { function: 'a.failed' })
    list({ runs: [failed] }, '', { permissions: { canOperate: false } })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))

    for (const name of ['Add time job', 'Add chain']) {
      const button = screen.getByRole('button', { name })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(described(button)).toBe('Needs operate permission')
    }

    for (const name of ['Requeue', 'Edit', 'Duplicate', 'Cancel', 'Delete', 'Force delete']) {
      const button = await within(panel(failed.id)).findByRole('button', { name })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(described(button)).toBe('Needs operate permission')
    }
  })

  it('requeues the checked runs the server allows, tags the refused one in its row, and offers View in the toast', async () => {
    const actor = user()
    const failed = [run('Failed', { function: 'a.first' }), run('Failed', { function: 'a.second' })]
    const done = run('Succeeded', { function: 'a.done' })
    const view = list({
      runs: [...failed, done],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.requeue'
          ? { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }
          : undefined,
    })

    await screen.findByRole('row', { name: /a\.first/ })
    for (const row of [...failed, done]) {
      await actor.click(screen.getByRole('checkbox', { name: `Select ${row.id}` }))
    }

    expect(screen.getByText('3 selected')).toBeTruthy()
    const requeue = await screen.findByRole('button', { name: 'Requeue 2 of 3' })
    await actor.click(requeue)
    const dialog = await screen.findByRole('alertdialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: /^Requeue 2/ }))

    const toast = await screen.findByText('Requeue: 2 done · 1 refused')
    expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.requeue')?.body).toMatchObject({
      ids: failed.map((row) => row.id),
    })
    expect(within(screen.getByRole('row', { name: /a\.done/ })).getByText('Not failed')).toBeTruthy()
    expect(
      within(toast.closest('[role="group"]') ?? toast.parentElement!).getByRole('button', { name: 'View' }),
    ).toBeTruthy()
  })

  it('starts an Operation confirm with the exact count when every matching failed run is checked', async () => {
    const actor = user()
    const failed = [run('Failed', { function: 'billing.charge' }), run('Failed', { function: 'billing.charge' })]
    const view = list(
      {
        runs: failed,
        extra: (request) => {
          if (request.path === 'jobs/runs') {
            return request.query?.after
              ? { items: [], nextCursor: null }
              : { items: failed, count: 312, countCapped: false, nextCursor: 'next' }
          }

          if (request.path === 'jobs/commands/jobs.requeue/confirm') {
            return {
              operationId: 'op1',
              count: 312,
              confirmation: 'typed',
              sample: [],
              token: 't',
              expiresAt: '',
              chunkSize: 500,
            }
          }

          return undefined
        },
      },
      '?status=Failed&function=billing.charge',
    )

    await screen.findAllByRole('row', { name: /billing\.charge/ })
    await actor.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await actor.click(await screen.findByRole('button', { name: 'Select all 312 matching' }))

    expect(screen.getByText('All 312 matching failed runs of billing.charge')).toBeTruthy()
    // A failed run has finished, so the Operation offers no Cancel, as the checked rows' bar does not.
    expect(screen.queryByRole('button', { name: /^Cancel/ })).toBeNull()
    await actor.click(screen.getByRole('button', { name: 'Requeue 312' }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))

    expect(await screen.findByRole('button', { name: 'Requeue 312 failed runs of billing.charge' })).toBeTruthy()
    expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
      filter: { statuses: ['Failed'], functions: ['billing.charge'] },
      reason: 'gateway fixed',
    })
  })

  it('opens the runs list filtered by the open run’s correlation id on T', async () => {
    const actor = user()
    const first = run('Succeeded', { function: 'orders.place', correlationId: 'corr-7f2' })
    const related = run('Failed', { function: 'billing.charge', correlationId: 'corr-7f2' })
    const other = run('Succeeded', { function: 'mail.send', correlationId: 'corr-other' })
    const view = list({ runs: [first, related, other] })

    await actor.click(await screen.findByRole('row', { name: /orders\.place/ }))
    await actor.keyboard('t')

    await waitFor(() => expect(screen.queryByRole('row', { name: /mail\.send/ })).toBeNull())
    expect(screen.getByRole('row', { name: /billing\.charge/ })).toBeTruthy()
    expect(view.location().pathname).toBe('/jobs/runs')
    expect(view.requests.filter((request) => request.path === 'jobs/runs').at(-1)?.query?.correlationId).toBe(
      'corr-7f2',
    )
    expect(screen.getByText('corr-7f2', { selector: 'b' })).toBeTruthy()
  })

  it('creates a chain with a failure branch, submits the tree, and opens the new chain', async () => {
    const actor = user()
    const created = run('Idle', { id: 'a0000000-0000-0000-0000-00000000000c', function: 'orders.place' })
    const rows = [run('Succeeded', { function: 'a.done' })]
    const view = list({
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

    const form = await screen.findByRole('region', { name: 'Add chain' })
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[0]!, 'billing.charge')
    await actor.type(within(form).getAllByRole('textbox', { name: /Arguments/ })[0]!, '{{"invoiceId": 42}')
    await actor.click(within(form).getByRole('button', { name: 'Add step' }))
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[1]!, 'billing.refund')
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Runs' }), 'On failure')
    await actor.click(within(form).getByRole('button', { name: 'Create chain' }))

    expect(await screen.findByRole('region', { name: `Run ${created.id}` })).toBeTruthy()
    expect(view.requests.find((request) => request.path === 'jobs/time-jobs')?.body).toEqual({
      function: 'billing.charge',
      request: { invoiceId: 42 },
      children: [{ function: 'billing.refund', runCondition: 'OnFailure' }],
      retries: 0,
    })
  })

  it('keeps the form open with the server’s message when it refuses the submit', async () => {
    const actor = user()
    list({
      extra: (request) => {
        if (request.method === 'POST' && request.path === 'jobs/time-jobs') {
          throw new DashboardApiError(400, "The request does not match billing.charge's argument type.")
        }

        return undefined
      },
    })

    await actor.click(await screen.findByRole('button', { name: 'Add time job' }))
    const form = await screen.findByRole('region', { name: 'Add time job' })
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Function' }), 'billing.charge')
    await actor.click(within(form).getByRole('button', { name: 'Create job' }))

    expect((await within(form).findByRole('alert')).textContent).toBe(
      "The request does not match billing.charge's argument type.",
    )
    expect(screen.getByRole('region', { name: 'Add time job' })).toBeTruthy()
  })

  it('keeps the function and arguments typed so far when Single job switches to Chain', async () => {
    const actor = user()
    const view = list({})

    await actor.click(await screen.findByRole('button', { name: 'Add time job' }))
    const single = await screen.findByRole('region', { name: 'Add time job' })
    await actor.selectOptions(within(single).getByRole('combobox', { name: 'Function' }), 'billing.charge')
    await actor.type(within(single).getByRole('textbox', { name: /Arguments/ }), '{{"invoiceId": 7}')
    await actor.click(within(single).getByRole('button', { name: 'Chain' }))

    const chain = await screen.findByRole('region', { name: 'Add chain' })
    expect(view.location().search).toBe('?create=chain')
    expect(within(chain).getByRole('button', { name: 'Chain' }).getAttribute('aria-pressed')).toBe('true')
    expect((within(chain).getByRole('combobox', { name: 'Function' }) as HTMLSelectElement).value).toBe(
      'billing.charge',
    )
    expect((within(chain).getByRole('textbox', { name: /Arguments/ }) as HTMLTextAreaElement).value).toBe(
      '{"invoiceId": 7}',
    )
    expect(within(chain).getByRole('region', { name: 'Steps after the first' })).toBeTruthy()
  })

  it('closes the add form from the panel’s Close', async () => {
    const actor = user()
    const view = list({}, '?create=time-job')

    await screen.findByRole('region', { name: 'Add time job' })
    await actor.click(screen.getByRole('button', { name: 'Close details' }))

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Add time job' })).toBeNull())
    expect(view.location().search).toBe('')
  })

  it('opens the Requeue Operation over the failed runs from the palette command, on the Failed list', async () => {
    let target = ''
    jobs.commands.find((command) => command.id === 'jobs.requeue-failed')!.run({ navigate: (to) => (target = to) })
    expect(target).toBe('runs?status=Failed&operation=requeue')

    const view = open({ runs: [run('Failed', { function: 'a.failed' })] })
    await screen.findByRole('heading', { level: 1, name: 'Dashboard' })
    await act(() => view.navigate(target))

    expect(await screen.findByRole('button', { name: 'Count rows' })).toBeTruthy()
    expect(view.location().pathname).toBe('/jobs/runs')
    expect(screen.getByRole('heading', { level: 1, name: 'Failed', hidden: true })).toBeTruthy()
  })

  it('drops a checked run a live change moves out of the list, so the bar and its action cover only the runs shown', async () => {
    const actor = user()
    const failed = [run('Failed', { function: 'a.first' }), run('Failed', { function: 'a.second' })]
    const view = list(
      {
        runs: failed,
        extra: (request) =>
          request.method === 'POST' && request.path === 'jobs/commands/jobs.requeue'
            ? { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }
            : undefined,
      },
      '?status=Failed',
    )

    await screen.findByRole('row', { name: /a\.first/ })
    for (const row of failed) {
      await actor.click(screen.getByRole('checkbox', { name: `Select ${row.id}` }))
    }
    expect(screen.getByText('2 selected')).toBeTruthy()

    const [first] = failed
    view.stream.push('change', change({ ...first!, status: 'Queued', version: '2' }))
    expect(await screen.findByText('1 selected')).toBeTruthy()

    // Back on the list, the run is no longer checked: it left the selection when it left the list.
    view.stream.push('change', change({ ...first!, version: '3' }))
    await screen.findByRole('row', { name: /a\.first/ })
    expect(screen.getByText('1 selected')).toBeTruthy()

    await actor.click(screen.getByRole('button', { name: 'Requeue' }))
    const dialog = await screen.findByRole('alertdialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: /^Requeue/ }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.requeue')?.body).toMatchObject({
        ids: [failed[1]!.id],
      }),
    )
  })

  it('drops the checked runs when the sidebar, a link, or Back changes the list', async () => {
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    const view = list({ runs: [failed, run('Succeeded', { function: 'a.done' })] }, '?status=Failed')

    await screen.findByRole('row', { name: /a\.failed/ })
    await actor.click(screen.getByRole('checkbox', { name: `Select ${failed.id}` }))
    expect(screen.getByText('1 selected')).toBeTruthy()

    await act(() => view.navigate('runs?status=Succeeded'))
    await screen.findByRole('row', { name: /a\.done/ })
    await act(() => view.navigate('runs?status=Failed'))
    await screen.findByRole('row', { name: /a\.failed/ })

    expect(screen.queryByText('1 selected')).toBeNull()
  })

  it('drops a refused run’s tag once a live change makes it a new version', async () => {
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    const done = run('Succeeded', { function: 'a.done' })
    const view = list({
      runs: [failed, done],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.requeue'
          ? { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }
          : undefined,
    })

    await screen.findByRole('row', { name: /a\.done/ })
    for (const row of [failed, done]) {
      await actor.click(screen.getByRole('checkbox', { name: `Select ${row.id}` }))
    }
    await actor.click(await screen.findByRole('button', { name: 'Requeue 1 of 2' }))
    const dialog = await screen.findByRole('alertdialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: /^Requeue/ }))

    await screen.findByText('Requeue: 1 done · 1 refused')
    const row = () => screen.getByRole('row', { name: /a\.done/ })
    expect(within(row()).getByText('Not failed')).toBeTruthy()

    view.stream.push('change', change({ ...done, status: 'Failed', version: '2' }))

    await waitFor(() => expect(within(row()).queryByText('Not failed')).toBeNull())
  })

  it('reads a status in any case as that status', async () => {
    const view = list(
      { runs: [run('Failed', { function: 'a.failed' }), run('Succeeded', { function: 'a.done' })] },
      '?status=failed',
    )

    expect(await screen.findByRole('heading', { level: 1, name: 'Failed' })).toBeTruthy()
    expect(await screen.findByRole('row', { name: /a\.failed/ })).toBeTruthy()
    expect(screen.queryByRole('row', { name: /a\.done/ })).toBeNull()
    expect(view.requests.find((request) => request.path === 'jobs/runs')?.query?.status).toBe('Failed')
    expect(screen.getByRole('group', { name: 'Filter by cause' })).toBeTruthy()
  })

  it('reads a status no run has as every status, with every filter offered', async () => {
    const view = list(
      { runs: [run('Failed', { function: 'a.failed' }), run('Succeeded', { function: 'a.done' })] },
      '?status=Bogus',
    )

    expect(await screen.findByRole('heading', { level: 1, name: 'All runs' })).toBeTruthy()
    expect(await screen.findByRole('row', { name: /a\.done/ })).toBeTruthy()
    expect(view.requests.find((request) => request.path === 'jobs/runs')?.query?.status).toBeUndefined()
    const filters = within(screen.getByRole('group', { name: 'Filters' }))
    for (const name of ['Kind', 'Function', 'Due']) {
      expect(filters.getByRole('button', { name: new RegExp(name) })).toBeTruthy()
    }
    // One time control sits in the row, so nothing waits behind More filters.
    expect(filters.queryByRole('button', { name: /More filters/ })).toBeNull()
  })

  it('opens a run the loaded page does not hold, reading it once by its id', async () => {
    const elsewhere = run('Succeeded', { function: 'a.elsewhere' })
    const view = list(
      { runs: [run('Failed', { function: 'a.failed' }), elsewhere] },
      `?status=Failed&open=${elsewhere.id}`,
    )

    // The panel loads first, then shows the run it read.
    await waitFor(() => expect(within(panel(elsewhere.id)).getAllByText('a.elsewhere').length).toBeGreaterThan(0))
    expect(screen.queryByRole('row', { name: /a\.elsewhere/ })).toBeNull()
    expect(view.requests.filter((request) => request.path === `jobs/runs/${elsewhere.id}`)).toHaveLength(1)
  })

  it('says a run opened by a link no longer exists on 404', async () => {
    list({ runs: [run('Failed')] }, '?status=Failed&open=00000000-0000-0000-0000-00000000dead')

    expect((await screen.findByRole('alert')).textContent).toBe('This run no longer exists.')
  })

  it('shows Stale at once on a list opened after the module went stale', async () => {
    const view = open({ runs: [run('Failed')] }, { path: '' })
    await screen.findByRole('region', { name: 'Processing now' })

    view.stream.push('module.stale', { module: 'jobs', stale: true })
    await act(() => view.navigate('runs'))

    expect(await screen.findByText('Stale. Live updates stopped, so these runs may be out of date.')).toBeTruthy()
  })
})

describe('jobs failed list', () => {
  const attention = (fn: string, fingerprint: string, count: number) => ({
    id: `failed:${fn}:${fingerprint}`,
    groupKey: `${fn}:${fingerprint}`,
    title: `${count} failed runs of ${fn}`,
    severity: 'critical',
    count,
    capped: false,
    recommended: { command: {}, filter: { functions: [fn], fingerprint, actionableOnly: true } },
  })

  it('lists the failed runs with their reason, and a cause chip filters by its fingerprint and shows its count', async () => {
    const actor = user()
    const declined = run('Failed', {
      function: 'billing.charge',
      failureFingerprint: 'v1:card',
      exceptionMessage: 'Card declined',
    })
    const timeout = run('Failed', {
      function: 'export.ledger',
      failureFingerprint: 'v1:io',
      exceptionMessage: 'Blob unreachable',
    })
    const view = list(
      {
        runs: [declined, timeout, run('Succeeded', { function: 'a.done' })],
        attention: [attention('billing.charge', 'v1:card', 1), attention('export.ledger', 'v1:io', 1)],
      },
      '?status=Failed',
    )

    expect(within(await screen.findByRole('row', { name: /billing\.charge/ })).getByText('Card declined')).toBeTruthy()
    expect(screen.queryByRole('row', { name: /a\.done/ })).toBeNull()

    const chips = await screen.findByRole('group', { name: 'Filter by cause' })
    await waitFor(() =>
      expect(
        within(chips)
          .getAllByRole('button')
          .map((chip) => chip.textContent),
      ).toEqual(['All2', 'billing.charge1', 'export.ledger1']),
    )
    expect(within(chips).getByRole('button', { name: /^All/ }).getAttribute('aria-pressed')).toBe('true')

    await actor.click(within(chips).getByRole('button', { name: /export\.ledger/ }))

    await waitFor(() => expect(screen.queryByRole('row', { name: /billing\.charge/ })).toBeNull())
    expect(screen.getByRole('row', { name: /export\.ledger/ })).toBeTruthy()
    expect(new URLSearchParams(view.location().search).get('fingerprint')).toBe('v1:io')
    expect(
      within(chips)
        .getByRole('button', { name: /export\.ledger/ })
        .getAttribute('aria-pressed'),
    ).toBe('true')
  })

  it('leads a reason with the exception’s short type name, and keeps the full text on hover', async () => {
    const typed = run('Failed', {
      function: 'a.typed',
      exceptionType: 'System.TimeoutException',
      exceptionMessage: 'Gateway timeout\n   at Billing.Charge()',
    })
    const stored = run('Failed', { function: 'a.plain', exceptionMessage: 'Lost by its node' })
    list({ runs: [typed, stored] }, '?status=Failed')

    const type = within(await screen.findByRole('row', { name: /a\.typed/ })).getByText('TimeoutException')
    const reason = type.closest('[title]')
    expect(reason?.textContent).toBe('TimeoutException: Gateway timeout')
    expect(reason?.getAttribute('title')).toBe('System.TimeoutException: Gateway timeout')

    // A reason the store wrote without a type reads as the message alone, in the cell's own tone.
    const plain = within(screen.getByRole('row', { name: /a\.plain/ })).getByText('Lost by its node')
    expect(plain.getAttribute('title')).toBe('Lost by its node')
  })

  it('presses only the chip of the function and fingerprint set, when two functions fail the same way', async () => {
    const actor = user()
    const charge = run('Failed', { function: 'billing.charge', failureFingerprint: 'v1:io' })
    const refund = run('Failed', { function: 'billing.refund', failureFingerprint: 'v1:io' })
    const view = list(
      {
        runs: [charge, refund],
        attention: [attention('billing.charge', 'v1:io', 1), attention('billing.refund', 'v1:io', 1)],
      },
      '?status=Failed&function=billing.charge&fingerprint=v1%3Aio',
    )

    const chips = await screen.findByRole('group', { name: 'Filter by cause' })
    const chip = (name: RegExp) => within(chips).getByRole('button', { name })
    await waitFor(() => expect(chip(/billing\.charge/).getAttribute('aria-pressed')).toBe('true'))
    expect(chip(/billing\.refund/).getAttribute('aria-pressed')).not.toBe('true')

    await actor.click(chip(/billing\.refund/))

    await waitFor(() => expect(new URLSearchParams(view.location().search).get('function')).toBe('billing.refund'))
    expect(new URLSearchParams(view.location().search).get('fingerprint')).toBe('v1:io')
  })
})

describe('jobs run page', () => {
  it('says the run no longer exists on 404', async () => {
    open({}, { path: 'runs/00000000-0000-0000-0000-00000000dead' })

    expect((await screen.findByRole('alert')).textContent).toContain('This run no longer exists.')
    expect(screen.getByRole('link', { name: 'Back to runs' }).getAttribute('href')).toBe('/jobs/runs')
  })

  it('shows the panel’s content as its own page, with nothing to close or detach', async () => {
    const failed = run('Failed', {
      function: 'a.failed',
      exceptionType: 'System.InvalidOperationException',
      exceptionMessage: 'Card declined',
      exceptionStack:
        'at MoveNext at offset 596 in /src/Sandbox/SandboxJobs.cs:96:13\nTimeoutException: The gateway did not answer.',
    })
    open(
      {
        runs: [failed],
        attempts: {
          [failed.id]: [
            {
              number: 1,
              node: 'node-b',
              startedAt: '2026-11-05T09:55:00Z',
              outcome: 'Failed',
              exceptionMessage: 'Card declined',
            },
          ],
        },
      },
      { path: `runs/${failed.id}` },
    )

    const page = await screen.findByRole('region', { name: `Run ${failed.id}` })
    expect(
      within(page)
        .getAllByRole('tab')
        .map((tab) => tab.textContent),
    ).toEqual(['Overview', 'Exception'])
    expect(
      within(page)
        .getByText(/MoveNext at offset 596/)
        .closest('pre')?.textContent,
    ).toBe(
      'System.InvalidOperationException: Card declined\n   at MoveNext at offset 596 in /src/Sandbox/SandboxJobs.cs:96:13\nTimeoutException: The gateway did not answer.',
    )
    expect(within(page).queryByRole('button', { name: 'Close details' })).toBeNull()
    expect(within(page).queryByRole('link', { name: 'Open in its own page' })).toBeNull()

    await user().click(within(page).getByRole('tab', { name: 'Overview' }))
    expect(await within(page).findByText('try 1 · node-b · Card declined', { exact: false })).toBeTruthy()
  })
})

describe('jobs runs list reads', () => {
  /** The last runs list request the view sent. */
  function filters() {
    return screen.getByRole('group', { name: 'Filters' })
  }

  /** The span a list read covers, from its `from` to its `before`. */
  function span(query: Readonly<Record<string, unknown>>) {
    return Date.parse(String(query.before)) - Date.parse(String(query.from))
  }

  /** Whether a preset's window ends at `at`, give or take the clock the test lets run. */
  function endsNear(query: Readonly<Record<string, unknown>>, at: number) {
    return Math.abs(Date.parse(String(query.before)) - at) < 5_000
  }

  function lastList(view: ReturnType<typeof list>) {
    return view.requests.filter((request) => request.path === 'jobs/runs').at(-1)?.query ?? {}
  }

  it('keeps a running run’s last progress through a stream drop, ages it live, and shows the stale notice', async () => {
    const running = run('InProgress', { function: 'export.ledger' })
    const view = list({ runs: [running] })
    await screen.findByRole('row', { name: /export\.ledger/ })

    await act(() => vi.advanceTimersByTimeAsync(5_000))
    view.stream.push(
      'change',
      // Stamped with the clock as it is now: real time also moves the fake clock, so a fixed instant would age by it.
      change({
        ...running,
        progressPercent: 62.5,
        progressMessage: 'step 25 of 40',
        progressUpdatedAt: new Date().toISOString(),
        version: '2',
      }),
    )
    const row = () => screen.getByRole('row', { name: /export\.ledger/ })
    await waitFor(() =>
      expect(within(row()).getByRole('progressbar', { name: 'Progress of export.ledger' })).toBeTruthy(),
    )

    act(() => view.stream.setStatus('stale'))
    await act(() => vi.advanceTimersByTimeAsync(5_000))

    // The cell keeps one line: the bar, the percentage, and the age; the handler's message stays in the bar's value text.
    expect(within(row()).getByRole('progressbar').getAttribute('aria-valuetext')).toBe('62.5 % · step 25 of 40')
    expect(within(row()).getByText('5 sec. ago')).toBeTruthy()
    expect(screen.getByText('Stale. Live updates stopped, so these runs may be out of date.')).toBeTruthy()

    await act(() => vi.advanceTimersByTimeAsync(10_000))
    expect(within(row()).getByText('15 sec. ago')).toBeTruthy()
  })

  it('keeps a finished run’s last progress as text, with no bar and no age', async () => {
    list({
      runs: [
        run('Succeeded', {
          function: 'export.ledger',
          progressPercent: 100,
          progressUpdatedAt: '2026-11-05T09:56:00Z',
        }),
      ],
    })

    const row = await screen.findByRole('row', { name: /export\.ledger/ })
    expect(within(row).getByText('100 %')).toBeTruthy()
    expect(within(row).queryByRole('progressbar')).toBeNull()
    expect(within(row).queryByText(/updated/)).toBeNull()
  })

  it('asks the server to sort by function on a header press and shows its first page in place of the loaded rows', async () => {
    const actor = user()
    const loaded = [run('Failed', { function: 'z.last' }), run('Failed', { function: 'm.middle' })]
    const sorted = [run('Failed', { function: 'a.first' })]
    const view = list({
      extra: (request) =>
        request.path === 'jobs/runs'
          ? request.query?.sort === 'function'
            ? { items: sorted, count: 3, countCapped: false, nextCursor: null }
            : { items: loaded, count: 3, countCapped: false, nextCursor: 'cursor-of-m' }
          : undefined,
    })
    await screen.findByRole('row', { name: /z\.last/ })

    const header = screen.getByRole('columnheader', { name: 'Function' })
    await actor.click(within(header).getByRole('button', { name: 'Function' }))

    await waitFor(() => expect(screen.queryByRole('row', { name: /z\.last/ })).toBeNull())
    expect(screen.getByRole('row', { name: /a\.first/ })).toBeTruthy()
    expect(header.getAttribute('aria-sort')).toBe('ascending')
    expect(lastList(view)).toMatchObject({ sort: 'function', direction: 'ascending' })
    // A cursor belongs to the order it was read in, so the sorted list starts again from its first page.
    expect(lastList(view).after).toBeUndefined()
  })

  it('offers no sort on the Failed list’s failed-at column, which the server cannot order by', async () => {
    list({ runs: [run('Failed', { function: 'a.failed' })] }, '?status=Failed')
    await screen.findByRole('row', { name: /a\.failed/ })

    expect(within(screen.getByRole('columnheader', { name: 'Failed at' })).queryByRole('button')).toBeNull()
    expect(
      within(screen.getByRole('columnheader', { name: 'Status' })).getByRole('button', { name: 'Status' }),
    ).toBeTruthy()
  })

  it('sends the search text once typing pauses, and scopes an Operation over every match to it', async () => {
    const actor = user()
    const failed = [run('Failed', { function: 'billing.refund' }), run('Failed', { function: 'billing.refund' })]
    const view = list(
      {
        runs: failed,
        extra: (request) => {
          if (request.path === 'jobs/runs') {
            return { items: failed, count: 40, countCapped: false, nextCursor: request.query?.after ? null : 'next' }
          }

          return request.path === 'jobs/commands/jobs.delete/confirm'
            ? {
                operationId: 'op1',
                count: 40,
                confirmation: 'confirm',
                sample: [],
                token: 't',
                expiresAt: '',
                chunkSize: 500,
              }
            : undefined
        },
      },
      '?status=Failed',
    )
    await screen.findAllByRole('row', { name: /billing\.refund/ })

    await actor.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'refund')
    await act(() => vi.advanceTimersByTimeAsync(300))
    await waitFor(() => expect(lastList(view).search).toBe('refund'))
    expect(
      view.requests.filter((request) => request.path === 'jobs/runs' && request.query?.search === 'refun'),
    ).toHaveLength(0)

    await actor.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await actor.click(await screen.findByRole('button', { name: 'Select all 40 matching' }))
    await actor.click(screen.getByRole('button', { name: 'Delete 40' }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'cleanup')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
        filter: { statuses: ['Failed'], search: 'refund' },
      }),
    )
  })

  it('keeps the due range, the search, and the sort in the URL, so a reload shows the same list', async () => {
    const actor = user()
    const fake = { runs: [run('Failed', { function: 'billing.refund' })] }
    const view = list(fake)
    await screen.findByRole('row', { name: /billing\.refund/ })

    await actor.type(screen.getByRole('searchbox', { name: 'Search runs' }), 'refund')
    await act(() => vi.advanceTimersByTimeAsync(300))
    await actor.click(
      within(screen.getByRole('columnheader', { name: 'Function' })).getByRole('button', { name: 'Function' }),
    )
    await actor.click(within(filters()).getByRole('button', { name: /Due/ }))
    await actor.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '7 days' }))

    const search = view.location().search
    expect(Object.fromEntries(new URLSearchParams(search))).toEqual({
      q: 'refund',
      sort: 'function',
      direction: 'ascending',
      range: '7d',
    })

    view.unmount()
    const reloaded = list(fake, search)
    await screen.findByRole('row', { name: /billing\.refund/ })

    expect((screen.getByRole('searchbox', { name: 'Search runs' }) as HTMLInputElement).value).toBe('refund')
    expect(screen.getByRole('columnheader', { name: 'Function' }).getAttribute('aria-sort')).toBe('ascending')
    expect(within(filters()).getByRole('button', { name: /Due/ }).textContent).toBe('Due7 days')
    expect(lastList(reloaded)).toMatchObject({ search: 'refund', sort: 'function', direction: 'ascending' })
    expect(span(lastList(reloaded))).toEqual(7 * 86_400_000)
  })

  it('works a preset link out when the view opens, so a shared link stays relative', async () => {
    // A link from before the range was kept by name still carries the instant it was picked at; the name wins.
    const link = '?range=24h&from=2026-10-01T00%3A00%3A00.000Z'
    const first = list({ runs: [] }, link)
    await waitFor(() => expect(lastList(first)).toHaveProperty('before'))
    expect(span(lastList(first))).toBe(86_400_000)
    expect(endsNear(lastList(first), now.getTime())).toBe(true)

    first.unmount()
    const later = now.getTime() + 3 * 3_600_000
    vi.setSystemTime(later)
    const reopened = list({ runs: [] }, link)
    await waitFor(() => expect(lastList(reopened)).toHaveProperty('before'))

    expect(span(lastList(reopened))).toBe(86_400_000)
    expect(endsNear(lastList(reopened), later)).toBe(true)
  })

  it('writes picked days as days, sent as the due bounds in the display zone', async () => {
    localStorage.setItem('headless-dashboard:time-zone', 'UTC')
    const actor = user()
    const view = list({ runs: [run('Succeeded', { function: 'a.done' })] })
    await screen.findByRole('row', { name: /a\.done/ })

    await actor.click(within(filters()).getByRole('button', { name: /Due/ }))
    const dialog = await screen.findByRole('dialog')
    await actor.click(within(dialog).getByRole('button', { name: 'Custom' }))
    await actor.click(within(dialog).getByRole('button', { name: /November 1, 2026/ }))
    await actor.click(within(dialog).getByRole('button', { name: /November 4, 2026/ }))

    await waitFor(() =>
      expect(lastList(view)).toMatchObject({ from: '2026-11-01T00:00:00.000Z', before: '2026-11-05T00:00:00.000Z' }),
    )
    expect(Object.fromEntries(new URLSearchParams(view.location().search))).toEqual({
      from: '2026-11-01',
      to: '2026-11-04',
    })
  })

  it('reads a range, sort, or direction it cannot read as the default', async () => {
    const view = list(
      { runs: [run('Succeeded', { function: 'a.done' })] },
      '?range=1y&from=2026-11-09&to=2026-11-01&sort=payload&direction=sideways',
    )
    await screen.findByRole('row', { name: /a\.done/ })

    expect(within(filters()).getByRole('button', { name: /Due/ }).textContent).toBe('Due')
    expect(lastList(view)).not.toHaveProperty('from')
    expect(lastList(view)).not.toHaveProperty('before')
    expect(lastList(view)).not.toHaveProperty('sort')

    await view.navigate('runs?sort=status&direction=sideways')
    await waitFor(() => expect(lastList(view)).toMatchObject({ sort: 'status', direction: 'ascending' }))
  })

  it('swaps custom days for a picked preset, and a preset for its days on Custom, keeping the search', async () => {
    localStorage.setItem('headless-dashboard:time-zone', 'UTC')
    const actor = user()
    const view = list({ runs: [] }, '?q=done&from=2026-11-01&to=2026-11-02')
    await waitFor(() =>
      expect(lastList(view)).toMatchObject({ from: '2026-11-01T00:00:00.000Z', before: '2026-11-03T00:00:00.000Z' }),
    )

    await actor.click(within(filters()).getByRole('button', { name: /Due/ }))
    const dialog = await screen.findByRole('dialog')
    await actor.click(within(dialog).getByRole('button', { name: '24 hr' }))

    expect(Object.fromEntries(new URLSearchParams(view.location().search))).toEqual({ q: 'done', range: '24h' })
    await waitFor(() => expect(span(lastList(view))).toBe(86_400_000))

    await actor.click(within(dialog).getByRole('button', { name: 'Custom' }))

    expect(Object.fromEntries(new URLSearchParams(view.location().search))).toEqual({
      q: 'done',
      from: '2026-11-04',
      to: '2026-11-05',
    })
    expect(lastList(view).search).toBe('done')
    expect((screen.getByRole('searchbox', { name: 'Search runs', hidden: true }) as HTMLInputElement).value).toBe(
      'done',
    )
  })

  it('shows the search a link or Back sets in the search box', async () => {
    const view = list({ runs: [run('Succeeded', { function: 'first.second' })] }, '?q=first')
    await screen.findByRole('row', { name: /first\.second/ })
    expect((screen.getByRole('searchbox', { name: 'Search runs' }) as HTMLInputElement).value).toBe('first')

    await view.navigate('runs?q=second')

    await waitFor(() =>
      expect((screen.getByRole('searchbox', { name: 'Search runs' }) as HTMLInputElement).value).toBe('second'),
    )
    expect(lastList(view).search).toBe('second')
  })

  it('shows a due range a link set on a state list as a chip that clears it, and Clear filters drops it', async () => {
    const actor = user()
    const view = list({ runs: [run('Failed', { function: 'a.failed' })] }, '?status=Failed&range=7d&q=a.')
    await screen.findByRole('row', { name: /a\.failed/ })

    await actor.click(screen.getByRole('button', { name: 'Clear due filter' }))

    expect(Object.fromEntries(new URLSearchParams(view.location().search))).toEqual({ status: 'Failed', q: 'a.' })
    await waitFor(() => expect(lastList(view)).not.toHaveProperty('from'))
  })

  it('reads a run’s attempt out of its first try and retries, and its retry intervals', async () => {
    const failed = run('Failed', { function: 'a.failed', retryCount: 2, retries: 3, retryIntervals: [30, 60] })
    const queued = run('Queued', { function: 'a.queued', retries: 1 })
    list({ runs: [failed, queued] })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))
    await user().click(within(panel(failed.id)).getByRole('tab', { name: 'Overview' }))
    expect(field(panel(failed.id), 'Attempt')).toBe('Attempt 3 of 4')
    expect(field(panel(failed.id), 'Retry intervals')).toBe('30 s, 60 s')

    await user().click(screen.getByRole('row', { name: /a\.queued/ }))
    expect(field(panel(queued.id), 'Attempt')).toBe('Not run yet, up to 2')
    expect(within(panel(queued.id)).queryByText('Retry intervals', { selector: 'dt' })).toBeNull()
  })
})

describe('jobs run actions', () => {
  it('duplicates a finished run at the execution time chosen and opens the new run', async () => {
    localStorage.setItem('headless-dashboard:time-zone', 'UTC')
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    const copy = run('Idle', {
      id: 'd0000000-0000-0000-0000-0000000000d1',
      function: 'a.failed',
      executionTime: '2026-11-06T10:00:00Z',
    })
    const rows = [failed]
    const view = list({
      runs: rows,
      extra: (request) => {
        if (request.method === 'POST' && request.path === 'jobs/commands/jobs.duplicate') {
          rows.push(copy)

          return { rows: [{ id: failed.id, outcome: 'done', created: { module: 'jobs', kind: 'run', id: copy.id } }] }
        }

        return undefined
      },
    })

    await actor.click(await screen.findByRole('row', { name: /a\.failed/ }))
    const duplicate = await within(panel(failed.id)).findByRole('button', { name: 'Duplicate' })
    await waitFor(() => expect(duplicate.getAttribute('aria-disabled')).toBeNull())
    await actor.click(duplicate)

    const dialog = await screen.findByRole('alertdialog', { name: 'Duplicate run' })
    await actor.click(await within(dialog).findByRole('spinbutton', { name: /month/i }))
    await actor.keyboard('11062026')
    await actor.keyboard('1000')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'retry with the fixed gateway')
    await actor.click(within(dialog).getByRole('button', { name: 'Duplicate run' }))

    expect(await screen.findByRole('region', { name: `Run ${copy.id}` })).toBeTruthy()
    expect(new URLSearchParams(view.location().search).get('open')).toBe(copy.id)
    expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.duplicate')?.body).toMatchObject({
      ids: [failed.id],
      arguments: { executionTime: '2026-11-06T10:00:00.000Z' },
      reason: 'retry with the fixed gateway',
    })
  })

  it('shows the server’s refusal of a duplicate in place, keeping the run open', async () => {
    const actor = user()
    const failed = run('Failed', { function: 'a.failed' })
    list({
      runs: [failed],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.duplicate'
          ? {
              rows: [
                { id: failed.id, outcome: 'skipped', reason: 'Its function is no longer registered on this host' },
              ],
            }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /a\.failed/ }))
    const duplicate = await within(panel(failed.id)).findByRole('button', { name: 'Duplicate' })
    await waitFor(() => expect(duplicate.getAttribute('aria-disabled')).toBeNull())
    await actor.click(duplicate)
    const dialog = await screen.findByRole('alertdialog', { name: 'Duplicate run' })
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'retry')
    await actor.click(within(dialog).getByRole('button', { name: 'Duplicate run' }))

    expect(await within(panel(failed.id)).findByText('Its function is no longer registered on this host')).toBeTruthy()
  })

  it('force deletes a running run whose cancel was requested only once a reason is given', async () => {
    const actor = user()
    const stuck = run('InProgress', { function: 'a.stuck', cancelRequested: true })
    const view = list({
      runs: [stuck],
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/commands/jobs.force-delete'
          ? { rows: [{ id: stuck.id, outcome: 'done' }] }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /a\.stuck/ }))
    const forceDelete = await within(panel(stuck.id)).findByRole('button', { name: 'Force delete' })
    await waitFor(() => expect(forceDelete.getAttribute('aria-disabled')).toBeNull())
    await actor.click(forceDelete)

    // A two-word label already names its effect, so the confirm keeps it as it is.
    const dialog = await screen.findByRole('alertdialog', { name: 'Force delete' })
    // The store row goes, but a worker still running the job is not stopped; the confirmation must say so.
    expect(within(dialog).getByText('Running code is not stopped.')).toBeTruthy()
    const confirm = within(dialog).getByRole('button', { name: 'Force delete' })
    expect(confirm.hasAttribute('disabled')).toBe(true)

    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'host lost, row stuck')
    expect(confirm.hasAttribute('disabled')).toBe(false)
    await actor.click(confirm)

    await waitFor(() =>
      expect(within(screen.getByRole('row', { name: /a\.stuck/ })).getByText('Deleting…')).toBeTruthy(),
    )
    expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.force-delete')?.body).toMatchObject({
      ids: [stuck.id],
      reason: 'host lost, row stuck',
    })
  })

  it('disables Force delete on a run whose cancel was not asked for, with the server’s reason', async () => {
    const running = run('InProgress', { function: 'a.running' })
    list({ runs: [running] })

    await user().click(await screen.findByRole('row', { name: /a\.running/ }))
    const forceDelete = await within(panel(running.id)).findByRole('button', { name: 'Force delete' })

    await waitFor(() => expect(described(forceDelete)).toBe('Cancel first'))
  })
})

describe('jobs create form', () => {
  it('offers every run condition for a chain step and sends the one chosen with the retry intervals', async () => {
    const actor = user()
    const view = list({
      extra: (request) =>
        request.method === 'POST' && request.path === 'jobs/time-jobs'
          ? { id: 'a0000000-0000-0000-0000-0000000000aa' }
          : undefined,
    })

    await actor.click(await screen.findByRole('button', { name: 'Add chain' }))
    const form = await screen.findByRole('region', { name: 'Add chain' })
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[0]!, 'billing.charge')
    await actor.type(within(form).getByRole('textbox', { name: 'Retries' }), '{backspace}3')
    await actor.type(within(form).getByRole('textbox', { name: /Retry intervals/ }), '30, 60 300')
    await actor.click(within(form).getByRole('button', { name: 'Add step' }))
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[1]!, 'billing.refund')

    const condition = within(form).getByRole('combobox', { name: 'Runs' }) as HTMLSelectElement
    expect([...condition.options].map((option) => option.value)).toEqual([
      'OnSuccess',
      'OnFailure',
      'OnCancelled',
      'OnFailureOrCancelled',
      'OnAnyCompletedStatus',
      'InProgress',
    ])
    await actor.selectOptions(condition, 'On failure or cancel')
    await actor.click(within(form).getByRole('button', { name: 'Create chain' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'jobs/time-jobs')?.body).toEqual({
        function: 'billing.charge',
        children: [{ function: 'billing.refund', runCondition: 'OnFailureOrCancelled' }],
        retries: 3,
        retryIntervals: [30, 60, 300],
      }),
    )
  })

  it('refuses retry intervals that are not whole seconds, without sending the form', async () => {
    const actor = user()
    const view = list({})

    await actor.click(await screen.findByRole('button', { name: 'Add time job' }))
    const form = await screen.findByRole('region', { name: 'Add time job' })
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Function' }), 'billing.charge')
    await actor.type(within(form).getByRole('textbox', { name: /Retry intervals/ }), '30, soon')
    await actor.click(within(form).getByRole('button', { name: 'Create job' }))

    expect(await within(form).findByText('Retry intervals must be whole seconds, such as 30, 60, 300.')).toBeTruthy()
    expect(view.requests.some((request) => request.path === 'jobs/time-jobs')).toBe(false)
  })
})
