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

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.queued/ })).getByText('In progress')).toBeTruthy())
    expect(view.requests.filter((request) => request.path === 'jobs/runs')).toHaveLength(before)
  })

  it('heads a state list with its title, served count, and what the state holds', async () => {
    list({ runs: [run('Idle'), run('Idle'), run('Failed')], counts: { Idle: 2 } }, '?status=Idle')

    const title = await screen.findByRole('heading', { level: 1, name: 'Idle' })
    const header = title.closest('header')!
    await waitFor(() => expect(within(header).getByText('2')).toBeTruthy())
    expect(within(header).getByText('Due later, waiting for a retry, or a chain step waiting for its parent.')).toBeTruthy()
    expect(screen.getAllByRole('row', { name: /billing\.charge/ })).toHaveLength(2)
  })

  it('reads each unset filter as Any, not None', async () => {
    list({ runs: [run('Failed')] })

    await screen.findByRole('row', { name: /billing\.charge/ })
    const unset = screen.getAllByRole('combobox').map((select) => (select as HTMLSelectElement).options[0]!.textContent)
    expect(unset).toEqual(['Any kind', 'Any function', 'Any time'])
  })

  it('counts a single live node in the singular', async () => {
    open(
      {
        runs: [],
        extra: (request) => (request.path === 'jobs/nodes' ? { items: [{ identity: 'node-a', state: 'Alive', claimedFunctions: null }] } : undefined),
      },
      { path: 'nodes' },
    )

    expect(await screen.findByText('1 live node.')).toBeTruthy()
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

    expect(await screen.findByText(/No runs match\. Clear filters or widen the time range\./)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Clear function filter' })).toBeTruthy()
    expect(screen.queryByRole('combobox')).toBeNull()

    await actor.click(screen.getByRole('button', { name: 'Clear filters' }))

    expect(await screen.findByRole('row', { name: /a\.failed/ })).toBeTruthy()
    expect(screen.getByRole('heading', { level: 1, name: 'Failed' })).toBeTruthy()
  })

  it('says a state list with no runs is empty in its own words', async () => {
    list({ runs: [run('Succeeded')] }, '?status=Cancelled')

    expect(await screen.findByText('No cancelled runs.')).toBeTruthy()
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

  it('opens a run in the docked panel, in the URL as open, with its exception and its attempts newest first', async () => {
    const failed = run('Failed', { function: 'a.failed', exceptionMessage: 'Gateway timeout', exceptionFrame: 'Billing.Charge()' })
    const view = list({
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
            failureFingerprint: 'v1:9f2c41',
          },
        ],
      },
    })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))

    expect(new URLSearchParams(view.location().search).get('open')).toBe(failed.id)
    const opened = panel(failed.id)
    expect(within(opened).getByRole('tab', { name: 'Overview', selected: true })).toBeTruthy()
    expect(within(opened).getByText('Gateway timeout').closest('pre')?.textContent).toBe('Gateway timeout\n   at Billing.Charge()')

    const history = await within(opened).findByRole('region', { name: 'History' })
    expect(within(history).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
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

    const dialog = await screen.findByRole('dialog', { name: 'Cancel run' })
    expect(within(dialog).getAllByRole('button').map((button) => button.textContent)).toEqual(['Back', 'Cancel run'])
  })

  it('reads the arguments when their tab opens, and says a run with none has no payload', async () => {
    const quiet = run('Succeeded', { function: 'a.quiet' })
    const actor = user()
    const view = list({ runs: [quiet] })

    await actor.click(await screen.findByRole('row', { name: /a\.quiet/ }))
    expect(view.requests.some((request) => request.path === 'payload')).toBe(false)
    await actor.click(within(panel(quiet.id)).getByRole('tab', { name: 'Arguments' }))

    expect(await screen.findByText('This run has no payload.')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('reads Cancelling… on a run with a pending cancel and keeps its stored status until the stream changes it', async () => {
    const running = run('InProgress', { function: 'a.running', cancelRequested: true })
    const view = list({ runs: [running] })

    const row = await screen.findByRole('row', { name: /a\.running/ })
    expect(within(row).getByText('Cancelling…').className).toContain('hd-tone-proc')

    await user().click(row)
    expect(field(panel(running.id), 'Status')).toBe('In progress, cancel requested')

    view.stream.push('change', change({ ...running, status: 'Cancelled', version: '2' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.running/ })).getByText('Cancelled')).toBeTruthy())
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
        request.method === 'POST' && request.path === 'jobs/commands/jobs.requeue' ? { rows: [{ id: failed.id, outcome: 'done' }] } : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /a\.failed/ }))
    const requeue = await within(panel(failed.id)).findByRole('button', { name: 'Requeue' })
    await waitFor(() => expect(requeue.getAttribute('aria-disabled')).toBeNull())
    await actor.click(requeue)

    const dialog = await screen.findByRole('dialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: 'Requeue run' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /a\.failed/ })).getByText('Requeuing…')).toBeTruthy())

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

    for (const name of ['Requeue', 'Cancel', 'Delete']) {
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
    const dialog = await screen.findByRole('dialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: /^Requeue 2/ }))

    const toast = await screen.findByText('Requeued 2 · 1 refused')
    expect(view.requests.find((request) => request.path === 'jobs/commands/jobs.requeue')?.body).toMatchObject({ ids: failed.map((row) => row.id) })
    expect(within(screen.getByRole('row', { name: /a\.done/ })).getByText('Not failed')).toBeTruthy()
    expect(within(toast.closest('[role="status"]') ?? toast.parentElement!).getByRole('button', { name: 'View' })).toBeTruthy()
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
              ? { items: [], count: 312, countCapped: false, nextCursor: null }
              : { items: failed, count: 312, countCapped: false, nextCursor: 'next' }
          }

          if (request.path === 'jobs/commands/jobs.requeue/confirm') {
            return { operationId: 'op1', count: 312, confirmation: 'typed', sample: [], token: 't', expiresAt: '', chunkSize: 500 }
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
    expect(view.requests.filter((request) => request.path === 'jobs/runs').at(-1)?.query?.correlationId).toBe('corr-7f2')
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

    const form = await screen.findByRole('complementary', { name: 'Add chain' })
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[0]!, 'billing.charge')
    await actor.type(within(form).getAllByRole('textbox', { name: /Arguments/ })[0]!, '{{"invoiceId": 42}')
    await actor.click(within(form).getByRole('button', { name: 'Add step after this' }))
    await actor.selectOptions(within(form).getAllByRole('combobox', { name: 'Function' })[1]!, 'billing.refund')
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Runs' }), 'On failure')
    await actor.click(within(form).getByRole('button', { name: 'Add chain' }))

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
    const form = await screen.findByRole('complementary', { name: 'Add time job' })
    await actor.selectOptions(within(form).getByRole('combobox', { name: 'Function' }), 'billing.charge')
    await actor.click(within(form).getByRole('button', { name: 'Add time job' }))

    expect((await within(form).findByRole('alert')).textContent).toBe("The request does not match billing.charge's argument type.")
    expect(screen.getByRole('complementary', { name: 'Add time job' })).toBeTruthy()
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
    const declined = run('Failed', { function: 'billing.charge', failureFingerprint: 'v1:card', exceptionMessage: 'Card declined' })
    const timeout = run('Failed', { function: 'export.ledger', failureFingerprint: 'v1:io', exceptionMessage: 'Blob unreachable' })
    const view = list(
      { runs: [declined, timeout, run('Succeeded', { function: 'a.done' })], attention: [attention('billing.charge', 'v1:card', 1), attention('export.ledger', 'v1:io', 1)] },
      '?status=Failed',
    )

    expect(within(await screen.findByRole('row', { name: /billing\.charge/ })).getByText('Card declined')).toBeTruthy()
    expect(screen.queryByRole('row', { name: /a\.done/ })).toBeNull()

    const chips = await screen.findByRole('group', { name: 'Filter by cause' })
    await waitFor(() => expect(within(chips).getAllByRole('button').map((chip) => chip.textContent)).toEqual(['All2', 'billing.charge1', 'export.ledger1']))
    expect(within(chips).getByRole('button', { name: /^All/ }).getAttribute('aria-pressed')).toBe('true')

    await actor.click(within(chips).getByRole('button', { name: /export\.ledger/ }))

    await waitFor(() => expect(screen.queryByRole('row', { name: /billing\.charge/ })).toBeNull())
    expect(screen.getByRole('row', { name: /export\.ledger/ })).toBeTruthy()
    expect(new URLSearchParams(view.location().search).get('fingerprint')).toBe('v1:io')
    expect(within(chips).getByRole('button', { name: /export\.ledger/ }).getAttribute('aria-pressed')).toBe('true')
  })
})

describe('jobs run page', () => {
  it('says the run no longer exists on 404', async () => {
    open({}, { path: 'runs/00000000-0000-0000-0000-00000000dead' })

    expect((await screen.findByRole('alert')).textContent).toBe('This run no longer exists.')
    expect(screen.getByRole('link', { name: 'Back to runs' }).getAttribute('href')).toBe('/jobs/runs')
  })

  it('shows the panel’s content as its own page, with nothing to close or detach', async () => {
    const failed = run('Failed', { function: 'a.failed', exceptionMessage: 'Card declined', exceptionFrame: 'MoveNext at offset 596 in /src/Sandbox/SandboxJobs.cs:96:13' })
    open(
      {
        runs: [failed],
        attempts: { [failed.id]: [{ number: 1, node: 'node-b', startedAt: '2026-11-05T09:55:00Z', outcome: 'Failed', exceptionMessage: 'Card declined' }] },
      },
      { path: `runs/${failed.id}` },
    )

    const page = await screen.findByRole('region', { name: `Run ${failed.id}` })
    expect(within(page).getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Overview', 'Arguments', 'Chain'])
    expect(within(page).getByText(/MoveNext at offset 596/).closest('pre')?.textContent).toBe(
      'Card declined\n   at MoveNext at offset 596 in /src/Sandbox/SandboxJobs.cs:96:13',
    )
    expect(within(page).queryByRole('button', { name: 'Close details' })).toBeNull()
    expect(within(page).queryByRole('link', { name: 'Open in its own page' })).toBeNull()
    expect(await within(page).findByText('try 1 · node-b · Card declined', { exact: false })).toBeTruthy()
  })
})
