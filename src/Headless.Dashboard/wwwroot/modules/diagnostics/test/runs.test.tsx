import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import diagnostics from '../src/module.ts'
import { runStatusMark, runStatuses, type Run, type RunDetail } from '../src/runs.ts'

const now = new Date('2026-11-05T10:00:00Z')

function run(id: string, status: string, overrides: Partial<Run> = {}): Run {
  return {
    id,
    status,
    attempts: status === 'Failed' ? 2 : 1,
    lastError: status === 'Failed' ? 'Gateway timeout' : undefined,
    traceId: `trace-${id}`,
    createdAt: '2026-11-05T09:50:00Z',
    updatedAt: '2026-11-05T09:56:00Z',
    version: '1',
    ...overrides,
  }
}

function detail(row: Run): RunDetail {
  return {
    ...row,
    timeline: [
      { kind: 'Created', at: row.createdAt },
      ...(row.status === 'Failed' ? [{ kind: 'Failed', at: row.updatedAt, text: 'Gateway timeout' }] : []),
    ],
  }
}

const base = {
  objectKind: 'run',
  permission: 'operate' as const,
  typedConfirmationAbove: 100,
  supportsFilter: true,
  canRun: true,
  arguments: { type: 'object' as const, required: [], properties: {} },
}

const commands: CommandDefinition[] = [
  { ...base, id: 'diagnostics.fail', label: 'Fail', confirmation: 'none', requiresReason: false, inverseCommandId: 'diagnostics.requeue' },
  { ...base, id: 'diagnostics.requeue', label: 'Requeue', confirmation: 'confirm', requiresReason: true },
  {
    ...base,
    id: 'diagnostics.add-note',
    label: 'Add note',
    confirmation: 'none',
    requiresReason: false,
    arguments: { type: 'object', required: ['text'], properties: { text: { type: 'string', title: 'Note', maxLength: 500 } } },
  },
]

function refusalOf(row: Run, commandId: string) {
  if (commandId === 'diagnostics.requeue') {
    return row.status === 'Failed' ? undefined : row.status === 'Running' ? 'Running - cancel instead' : 'Not failed'
  }

  return commandId === 'diagnostics.fail' && row.status === 'Failed' ? 'Already failed' : undefined
}

/** A fake of the module's server: its runs API and the shell's command endpoints for it. */
function server(rows: Run[], extra?: (request: ApiRequest) => unknown) {
  return (request: ApiRequest) => {
    const answered = extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    if (request.method === 'GET' && request.path === 'diagnostics/runs') {
      const status = request.query?.status
      const items = rows.filter((row) => !status || row.status === status)

      return { items, count: items.length, countCapped: false }
    }

    const one = /^diagnostics\/runs\/(.+)$/.exec(request.path)

    if (request.method === 'GET' && one) {
      const row = rows.find((candidate) => candidate.id === decodeURIComponent(one[1]!))

      if (!row) {
        throw new DashboardApiError(404, 'Not found')
      }

      return detail(row)
    }

    if (request.path === 'diagnostics/commands') {
      return { commands }
    }

    if (request.path === 'diagnostics/commands/allowed') {
      const ids = [request.query?.id ?? []].flat()

      return {
        objects: ids.map((id) => {
          const row = rows.find((candidate) => candidate.id === id)!

          return {
            kind: 'run',
            id,
            commands: commands.map((command) => {
              const reason = refusalOf(row, command.id)

              return { id: command.id, allowed: !reason, disabledReason: reason }
            }),
          }
        }),
      }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

function open(rows: Run[], options: RenderModuleOptions & { extra?: (request: ApiRequest) => unknown } = {}) {
  return renderModule(diagnostics, { api: server(rows, options.extra), ...options })
}

function change(row: Run) {
  return { module: 'diagnostics', kind: 'run', id: row.id, topic: 'diagnostics.run', version: row.version, projection: { ...row } }
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
})

function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}

describe('diagnostics runs list', () => {
  it('lists runs with each status as mark and label, and subscribes to the run topic', async () => {
    const view = open([run('run-000001', 'Failed'), run('run-000002', 'Running'), run('run-000003', 'Succeeded'), run('run-000004', 'Queued')])

    const failed = await screen.findByRole('row', { name: /run-000001/ })
    expect(within(failed).getByText('Failed')).toBeTruthy()
    expect(within(failed).getByText('Gateway timeout')).toBeTruthy()
    expect(within(failed).getByText('4 minutes ago')).toBeTruthy()
    expect(within(screen.getByRole('row', { name: /run-000002/ })).getByText('Running')).toBeTruthy()
    expect(within(screen.getByRole('banner')).getByText('4')).toBeTruthy()
    expect(view.stream.topics()).toContain('diagnostics.run')
  })

  it('opens a run in the docked panel, filled from the cached row at once, then adds the timeline', async () => {
    let answer: (value: RunDetail) => void = () => undefined
    const row = run('run-000001', 'Failed')
    open([row], {
      extra: (request) =>
        request.path === 'diagnostics/runs/run-000001' ? new Promise<RunDetail>((resolve) => (answer = resolve)) : undefined,
    })

    await user().click(await screen.findByRole('row', { name: /run-000001/ }))

    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    expect(within(panel).getByText('trace-run-000001')).toBeTruthy()
    expect(within(panel).getByText('Attempts').nextElementSibling?.textContent).toBe('2')
    expect(within(panel).getByText('Loading the timeline…')).toBeTruthy()

    act(() => answer(detail(row)))

    const timeline = await within(panel).findByRole('region', { name: 'Timeline' })
    expect(within(timeline).getAllByRole('row').map((item) => item.textContent)).toEqual([
      'Created 10 minutes ago',
      'Failed 4 minutes ago Gateway timeout',
    ])
  })

  it('opens a run the loaded page does not hold, read by its id', async () => {
    const onPage = run('run-000002', 'Queued')
    const offPage = run('run-000017', 'Failed')
    const view = open([onPage, offPage], {
      path: '?open=run-000017',
      extra: (request) =>
        request.method === 'GET' && request.path === 'diagnostics/runs' ? { items: [onPage], count: 2, countCapped: false } : undefined,
    })

    // The panel reads `Loading the run…` first, then the run's own panel replaces it.
    await waitFor(() => expect(within(screen.getByRole('region', { name: 'Run run-000017' })).getByText('trace-run-000017')).toBeTruthy())
    const panel = screen.getByRole('region', { name: 'Run run-000017' })
    expect(await within(panel).findByRole('region', { name: 'Timeline' })).toBeTruthy()
    expect(screen.queryByRole('row', { name: /run-000017/ })).toBeNull()
    expect(view.requests.filter((request) => request.path === 'diagnostics/runs/run-000017')).toHaveLength(1)
  })

  it('says how runs get there when there are none, and Add run adds and opens one', async () => {
    const rows: Run[] = []
    const view = open(rows, {
      extra: (request) => {
        if (request.method === 'POST' && request.path === 'diagnostics/runs') {
          const added = run('run-000001', 'Queued')
          rows.push(added)

          return detail(added)
        }

        return undefined
      },
    })

    expect(await screen.findByText('No runs yet. Add run creates one.')).toBeTruthy()

    await user().click(screen.getByRole('button', { name: 'Add run' }))

    expect(await screen.findByRole('region', { name: 'Run run-000001' })).toBeTruthy()
    expect(view.requests.filter((request) => request.method === 'POST' && request.path === 'diagnostics/runs')).toHaveLength(1)
  })

  it('shows loading only after 150 ms', async () => {
    open([], { extra: (request) => (request.path === 'diagnostics/runs' ? new Promise(() => undefined) : undefined) })
    await screen.findByRole('textbox', { name: 'Error contains' })

    await act(() => vi.advanceTimersByTimeAsync(100))
    expect(screen.queryByRole('status', { name: 'Loading runs' })).toBeNull()

    await act(() => vi.advanceTimersByTimeAsync(60))
    expect(screen.getByRole('status', { name: 'Loading runs' })).toBeTruthy()
  })

  it('shows a failed load with Retry, never an empty list', async () => {
    let fail = true
    const rows = [run('run-000001', 'Queued')]
    open(rows, {
      extra: (request) => {
        if (request.path === 'diagnostics/runs' && fail) {
          throw new DashboardApiError(503, 'The diagnostic store did not answer. Try again.')
        }

        return undefined
      },
    })

    expect((await screen.findByRole('alert')).textContent).toBe('The diagnostic store did not answer. Try again.')
    expect(screen.queryByText('No runs yet. Add run creates one.')).toBeNull()

    fail = false
    await user().click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('row', { name: /run-000001/ })).toBeTruthy()
  })

  it('patches a row from a live change and holds new rows behind a notice', async () => {
    const view = open([run('run-000001', 'Queued')])
    await screen.findByRole('row', { name: /run-000001/ })

    view.stream.push('change', change(run('run-000001', 'Running', { version: '2' })))
    view.stream.push('change', change(run('run-000009', 'Queued')))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /run-000001/ })).getByText('Running')).toBeTruthy())
    expect(screen.getByRole('button', { name: '1 new' })).toBeTruthy()
    expect(screen.queryByRole('row', { name: /run-000009/ })).toBeNull()
  })

  it('shows Stale while the stream is down', async () => {
    const view = open([run('run-000001', 'Queued')])
    await screen.findByRole('row', { name: /run-000001/ })

    view.stream.setStatus('stale')

    expect(screen.getByText('Stale. Live updates stopped, so these runs may be out of date.')).toBeTruthy()
  })

  it('disables every operate action for a viewer, with the reason', async () => {
    open([run('run-000001', 'Failed')], { permissions: { canOperate: false } })

    await user().click(await screen.findByRole('row', { name: /run-000001/ }))

    for (const name of ['Add run', 'Requeue failed runs']) {
      const button = screen.getByRole('button', { name })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(document.getElementById(button.getAttribute('aria-describedby')!)?.textContent).toBe('Needs operate permission')
    }

    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    const requeue = await within(panel).findByRole('button', { name: 'Requeue' })
    expect(requeue.getAttribute('aria-disabled')).toBe('true')
  })

  it('refuses Requeue on a running run with the server’s reason', async () => {
    open([run('run-000001', 'Running')])

    await user().click(await screen.findByRole('row', { name: /run-000001/ }))

    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    const requeue = await within(panel).findByRole('button', { name: 'Requeue' })
    await waitFor(() => expect(requeue.getAttribute('aria-disabled')).toBe('true'))
    expect(document.getElementById(requeue.getAttribute('aria-describedby')!)?.textContent).toBe('Running - cancel instead')
  })

  it('does nothing on R when the server refuses the selected run’s requeue, and announces why', async () => {
    const actor = user()
    const shell = open([run('run-000001', 'Running')])

    await actor.click(await screen.findByRole('row', { name: /run-000001/ }))
    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    await waitFor(() => expect(within(panel).getByRole('button', { name: 'Requeue' }).getAttribute('aria-disabled')).toBe('true'))

    await actor.keyboard('r')

    expect(within(panel).getAllByRole('status').map((region) => region.textContent)).toContain('Running - cancel instead')
    expect(shell.requests.some((request) => request.method === 'POST')).toBe(false)
  })

  it('keeps every action disabled until the server says it can run now', async () => {
    let answer: (value: unknown) => void = () => {}
    open([run('run-000001', 'Queued')], {
      extra: (request) =>
        request.path === 'diagnostics/commands/allowed'
          ? new Promise((resolve) => {
              answer = resolve
            })
          : undefined,
    })

    await user().click(await screen.findByRole('row', { name: /run-000001/ }))
    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    const fail = await within(panel).findByRole('button', { name: 'Fail' })

    expect(fail.getAttribute('aria-disabled')).toBe('true')
    expect(document.getElementById(fail.getAttribute('aria-describedby')!)?.textContent).toBe('Checking whether this can run…')

    act(() =>
      answer({
        objects: [
          { kind: 'run', id: 'run-000001', commands: commands.map((command) => ({ id: command.id, allowed: true })) },
        ],
      }),
    )
    await waitFor(() => expect(within(panel).getByRole('button', { name: 'Fail' }).getAttribute('aria-disabled')).toBeNull())
  })

  it('keeps actions disabled and says so when the server cannot say whether they can run', async () => {
    open([run('run-000001', 'Queued')], {
      extra: (request) => {
        if (request.path === 'diagnostics/commands/allowed') {
          throw new DashboardApiError(503, 'Unavailable')
        }

        return undefined
      },
    })

    await user().click(await screen.findByRole('row', { name: /run-000001/ }))
    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    const fail = await within(panel).findByRole('button', { name: 'Fail' })

    await waitFor(() =>
      expect(document.getElementById(fail.getAttribute('aria-describedby')!)?.textContent).toBe(
        'Could not check whether this can run. Reload to try again.',
      ),
    )
    expect(fail.getAttribute('aria-disabled')).toBe('true')
  })

  it('reads Requeuing… after a requeue until the stream sets the real status', async () => {
    const actor = user()
    const row = run('run-000001', 'Failed')
    const view = open([row], {
      extra: (request) =>
        request.method === 'POST' && request.path === 'diagnostics/commands/diagnostics.requeue'
          ? { rows: [{ id: 'run-000001', outcome: 'done' }] }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /run-000001/ }))
    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    const requeue = await within(panel).findByRole('button', { name: 'Requeue' })
    await waitFor(() => expect(requeue.getAttribute('aria-disabled')).toBeNull())
    await actor.click(requeue)

    const dialog = await screen.findByRole('dialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(within(dialog).getByRole('button', { name: 'Requeue run' }))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /run-000001/ })).getByText('Requeuing…')).toBeTruthy())
    const call = view.requests.find((request) => request.path === 'diagnostics/commands/diagnostics.requeue')
    expect(call?.body).toMatchObject({ ids: ['run-000001'], reason: 'gateway fixed', confirmed: true })

    view.stream.push('change', change(run('run-000001', 'Queued', { version: '2' })))

    await waitFor(() => expect(within(screen.getByRole('row', { name: /run-000001/ })).getByText('Queued')).toBeTruthy())
    expect(screen.queryByText('Requeuing…')).toBeNull()
  })

  it('runs Fail at once and offers Undo, which requeues', async () => {
    const actor = user()
    const view = open([run('run-000001', 'Queued')], {
      extra: (request) =>
        request.method === 'POST' && request.path.startsWith('diagnostics/commands/diagnostics.')
          ? { rows: [{ id: 'run-000001', outcome: 'done' }] }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /run-000001/ }))
    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    const fail = await within(panel).findByRole('button', { name: 'Fail' })
    await waitFor(() => expect(fail.getAttribute('aria-disabled')).toBeNull())
    await actor.click(fail)

    expect(screen.queryByRole('dialog')).toBeNull()
    await actor.click(await screen.findByRole('button', { name: 'Undo' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'diagnostics/commands/diagnostics.requeue')?.body).toMatchObject({
        ids: ['run-000001'],
      }),
    )
  })

  it('adds a note through the argument form', async () => {
    const actor = user()
    const view = open([run('run-000001', 'Queued')], {
      extra: (request) =>
        request.method === 'POST' && request.path === 'diagnostics/commands/diagnostics.add-note'
          ? { rows: [{ id: 'run-000001', outcome: 'done' }] }
          : undefined,
    })

    await actor.click(await screen.findByRole('row', { name: /run-000001/ }))
    const panel = screen.getByRole('region', { name: 'Run run-000001' })
    await actor.click(await within(panel).findByRole('button', { name: 'Add note' }))

    const dialog = await screen.findByRole('dialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /Note/ }), 'checked the gateway')
    await actor.click(within(dialog).getByRole('button', { name: 'Add note' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'diagnostics/commands/diagnostics.add-note')?.body).toMatchObject({
        ids: ['run-000001'],
        arguments: { text: 'checked the gateway' },
      }),
    )
  })

  it('requeues the failed runs as an Operation over the failed filter', async () => {
    const actor = user()
    const view = open([run('run-000001', 'Failed')], {
      extra: (request) => {
        if (request.path === 'diagnostics/commands/diagnostics.requeue/confirm') {
          return { operationId: 'op1', count: 1, confirmation: 'confirm', sample: [], token: 't', expiresAt: '', chunkSize: 500 }
        }

        if (request.path === 'diagnostics/commands/diagnostics.requeue/chunks') {
          return { operationId: 'op1', rows: [{ id: 'run-000001', outcome: 'done' }], done: true }
        }

        return undefined
      },
    })

    await screen.findByRole('row', { name: /run-000001/ })
    await actor.click(screen.getByRole('button', { name: 'Requeue failed runs' }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'gateway fixed')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))
    await actor.click(await screen.findByRole('button', { name: 'Requeue 1 failed runs' }))

    await screen.findByText('Requeue failed runs: Completed')
    expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
      filter: { status: 'Failed' },
      reason: 'gateway fixed',
    })
  })

  it('opens the Requeue Operation from the palette command', async () => {
    let target = ''
    const command = diagnostics.commands.find((candidate) => candidate.id === 'diagnostics.requeue-failed')!
    command.run({ navigate: (to) => (target = to) })

    const view = open([run('run-000001', 'Failed')])
    await screen.findByRole('row', { name: /run-000001/ })
    await act(() => view.navigate(target))

    expect(await screen.findByRole('button', { name: 'Count rows' })).toBeTruthy()
    // The confirm dialog hides the view from assistive technology while it is open.
    expect(screen.getByRole('combobox', { name: 'Status', hidden: true })).toHaveProperty('value', 'Failed')
  })

  it('checks the focused row with X, and Requeue on the checked rows asks for a reason', async () => {
    const actor = user()
    open([run('run-000001', 'Failed'), run('run-000002', 'Failed')])

    await actor.click(await screen.findByRole('row', { name: /run-000001/ }))
    act(() => screen.getByRole('row', { name: /run-000002/ }).focus())
    await actor.keyboard('x')

    const header = screen.getByRole('banner')
    expect(within(header).getByText('1 selected')).toBeTruthy()
    const requeue = await within(header).findByRole('button', { name: 'Requeue' })
    await waitFor(() => expect(requeue.getAttribute('aria-disabled')).toBeNull())
    await actor.click(requeue)

    expect(await screen.findByRole('dialog')).toBeTruthy()
  })

  it('counts only the checked runs the list still shows after a live change moves one out of it', async () => {
    const actor = user()
    const view = open([run('run-000001', 'Failed'), run('run-000002', 'Failed')], { path: '?status=Failed' })

    await screen.findByRole('row', { name: /run-000001/ })
    for (const id of ['run-000001', 'run-000002']) {
      await actor.click(screen.getByRole('checkbox', { name: `Select ${id}` }))
    }
    const header = screen.getByRole('banner')
    expect(within(header).getByText('2 selected')).toBeTruthy()

    view.stream.push('change', change(run('run-000001', 'Queued', { version: '2' })))

    expect(await within(header).findByText('1 selected')).toBeTruthy()
  })
})

describe('diagnostics run page', () => {
  it('shows the run’s header, fields, timeline, and payload on request', async () => {
    const actor = user()
    open([run('run-000001', 'Failed')], {
      path: 'runs/run-000001',
      extra: (request) =>
        request.path === 'payload'
          ? { content: '{"amount":12}', contentType: 'application/json', headers: { 'x-api-key': '***' } }
          : undefined,
    })

    const page = await screen.findByRole('region', { name: 'Run run-000001' })
    expect(within(page).getByRole('heading', { name: 'Run' })).toBeTruthy()
    expect(within(page).queryByRole('link', { name: 'Open in its own page' })).toBeNull()
    expect(within(within(page).getByRole('region', { name: 'Timeline' })).getAllByRole('row')).toHaveLength(2)

    await actor.click(within(page).getByRole('button', { name: 'Show payload' }))

    expect(await within(page).findByText('***')).toBeTruthy()
  })

  it('shows a timeline event selected with Enter under the timeline, keeping focus in the timeline', async () => {
    const actor = user()
    open([run('run-000001', 'Failed')], { path: 'runs/run-000001' })

    const page = await screen.findByRole('region', { name: 'Run run-000001' })
    await within(page).findByRole('region', { name: 'Timeline' })

    within(page).getAllByRole('row')[0]!.focus()
    await actor.keyboard('{ArrowDown}{Enter}')

    const event = await screen.findByRole('region', { name: 'Event Failed' })
    expect(within(event).getByText('Gateway timeout')).toBeTruthy()
    expect(document.activeElement).toBe(within(page).getAllByRole('row')[1])
  })

  it('says the run is gone when the server answers 404', async () => {
    open([], { path: 'runs/run-000404' })

    expect((await screen.findByRole('alert')).textContent).toContain('No run run-000404.')
    expect(screen.getByRole('link', { name: 'Back to runs' })).toBeTruthy()
  })
})

describe('diagnostics state roles', () => {
  it('maps every run state to its state role, and a pending requeue to queued', () => {
    expect(Object.fromEntries(runStatuses.map((status) => [status, runStatusMark(status).tone]))).toEqual({
      Queued: 'enq',
      Running: 'proc',
      Succeeded: 'ok',
      Failed: 'fail',
    })
    expect(runStatusMark('Failed', true)).toEqual({ tone: 'enq', label: 'Requeuing…' })
    expect(runStatusMark('Paused').tone).toBe('off')
  })
})
