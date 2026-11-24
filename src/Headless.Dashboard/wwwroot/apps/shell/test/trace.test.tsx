import { traceAddress } from '@headless/dashboard-sdk/components'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { Trace, TraceRow, TraceSection } from '../src/shell-data.ts'
import { chunkUrl, moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { emptyAggregates, renderShell, type ApiHandler } from './harness.tsx'

const jobs = moduleManifest('jobs', { displayName: 'Jobs' })
const messaging = moduleManifest('messaging', {
  displayName: 'Messaging',
  routes: [{ path: 'published', chunk: chunkUrl('messaging', 'published') }],
})
const modules = [jobs, messaging]

function section(id: string, displayName: string, overrides: Partial<TraceSection> = {}): TraceSection {
  return { id, displayName, stale: false, limited: false, count: 0, ...overrides }
}

function row(module: string, kind: string, id: string, depth: number, overrides: Partial<TraceRow> = {}): TraceRow {
  return {
    module,
    target: { module, kind, id },
    title: `${kind} ${id}`,
    status: 'Succeeded',
    statusRole: 'ok',
    at: '2026-11-05T09:50:00Z',
    timestampKind: 'ran',
    depth,
    tags: [],
    ...overrides,
  }
}

/** A job run that published a message, which two subscribers received. */
const jobAndMessage: Trace = {
  modules: [section('jobs', 'Jobs', { count: 1 }), section('messaging', 'Messaging', { count: 3 })],
  rows: [
    row('jobs', 'run', 'r1', 0, { title: 'billing.charge' }),
    row('messaging', 'published', 'm1', 1, { title: 'orders.placed' }),
    row('messaging', 'received', 'g1', 2, { title: 'orders.placed' }),
    row('messaging', 'received', 'g2', 2, { title: 'orders.placed', tags: ['clock-skew'] }),
  ],
  hidden: 0,
}

/** Answers `trace` with `answer`, or with what it returns for the request, and every other aggregate as empty. */
function traceApi(answer: Trace | ((request: ApiRequest) => unknown)): ApiHandler {
  return (request) => {
    if (request.path === 'trace') {
      return typeof answer === 'function' ? answer(request) : answer
    }

    return emptyAggregates(request)
  }
}

function renderTrace(answer: Trace | ((request: ApiRequest) => unknown), path = '/trace#ord-7') {
  return renderShell(shellState(modules), { path, files: moduleFiles(modules), api: traceApi(answer) })
}

function traceRequests(requests: ApiRequest[]) {
  return requests.filter((request) => request.path === 'trace')
}

function sources() {
  return screen.getByRole('list', { name: 'Modules on this trace' })
}

describe('trace page', () => {
  it('reads back exactly the id a trace address was built from, even one holding # and %', async () => {
    const id = 'ord#7 %41 c-42'
    const shell = renderTrace(jobAndMessage, traceAddress(id))

    await waitFor(() =>
      expect(shell.requests.find((request) => request.path === 'trace')?.body).toEqual({ correlationId: id }),
    )
  })

  it('posts the fragment’s id in the body, never the URL, and draws every module’s rows in one tree', async () => {
    const shell = renderTrace(jobAndMessage)

    const tree = await screen.findByRole('treegrid', { name: 'Trace of ord-7' })
    expect(traceRequests(shell.requests)).toEqual([{ method: 'POST', path: 'trace', body: { correlationId: 'ord-7' } }])
    expect(screen.getByRole('heading', { level: 1, name: 'Trace' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Copy the correlation id' })).toBeTruthy()

    // aria-level counts from 1, so it is the server's depth plus one: depths 0, 1, 2, 2.
    const rows = within(tree).getAllByRole('row')
    expect(rows.map((element) => element.getAttribute('aria-level'))).toEqual(['1', '2', '3', '3'])
    expect(rows[0]!.textContent).toContain('billing.charge')
    expect(rows[0]!.textContent).toContain('Jobs run r1')
    expect(rows[3]!.textContent).toContain('Messaging received g2')
    expect(rows[3]!.textContent).toContain('clock skew')
  })

  it('gives each row the level of its depth, however deep the tree goes', async () => {
    renderTrace({
      ...jobAndMessage,
      rows: [0, 1, 2, 3, 4, 5].map((depth) => row('jobs', 'run', `r${depth}`, depth)),
    })

    const rows = within(await screen.findByRole('treegrid')).getAllByRole('row')
    expect(rows.map((element) => element.getAttribute('aria-level'))).toEqual(['1', '2', '3', '4', '5', '6'])
    expect(rows[5]!.textContent).toContain('level 6')
  })

  it('keeps the Jobs rows when Messaging is stale, and its Retry posts the whole trace again', async () => {
    const user = userEvent.setup()
    const shell = renderTrace({
      modules: [section('jobs', 'Jobs', { count: 1 }), section('messaging', 'Messaging', { stale: true })],
      rows: [row('jobs', 'run', 'r1', 0, { title: 'billing.charge' })],
      hidden: 0,
    })

    await screen.findByRole('treegrid')
    const line = within(sources()).getByText('Messaging').closest('li')!
    expect(line.textContent).toContain('Stale')
    expect(screen.getAllByRole('row')[0]!.textContent).toContain('billing.charge')

    await user.click(within(line).getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(traceRequests(shell.requests)).toHaveLength(2))
    expect(traceRequests(shell.requests)[1]!.body).toEqual({ correlationId: 'ord-7' })
  })

  it('links a limited module’s 500+ to its own list, with the filter in history state rather than the URL', async () => {
    const user = userEvent.setup()
    const shell = renderTrace({
      modules: [
        section('jobs', 'Jobs', {
          count: 500,
          limited: true,
          list: { path: 'runs', state: { correlationId: 'ord-7' } },
        }),
        section('messaging', 'Messaging', {
          count: 500,
          limited: true,
          list: { path: 'published', state: { correlationId: 'ord-7' } },
        }),
      ],
      rows: [row('jobs', 'run', 'r1', 0)],
      hidden: 0,
    })

    await screen.findByRole('treegrid')
    const jobsLink = within(sources()).getByRole('link', { name: '500+ in Jobs, open the list' })
    expect(within(sources()).getByRole('link', { name: '500+ in Messaging, open the list' })).toBeTruthy()

    await user.click(jobsLink)

    expect(await screen.findByRole('heading', { name: 'Jobs runs view' })).toBeTruthy()
    expect(shell.router.state.location.pathname).toBe('/jobs/runs')
    expect(shell.router.state.location.search).toBe('')
    expect(shell.router.state.location.state).toEqual({ correlationId: 'ord-7' })

    await shell.router.navigate('/trace#ord-7')
    const list = await screen.findByRole('list', { name: 'Modules on this trace' })
    await user.click(within(list).getByRole('link', { name: '500+ in Messaging, open the list' }))
    expect(await screen.findByRole('heading', { name: 'Messaging published view' })).toBeTruthy()
    expect(shell.router.state.location.pathname).toBe('/messaging/published')
    expect(shell.router.state.location.state).toEqual({ correlationId: 'ord-7' })
  })

  it('names the sources that did not answer, and how many modules the user may not view', async () => {
    renderTrace({
      modules: [section('messaging', 'Messaging', { count: 2, unavailable: ['store-b', 'node-2'] })],
      rows: [row('messaging', 'published', 'm1', 0)],
      hidden: 1,
    })

    await screen.findByRole('treegrid')
    expect(sources().textContent).toContain('store-b, node-2 did not answer')
    expect(within(sources()).getByText('1 module hidden by your permissions')).toBeTruthy()
  })

  it('says no object carries an id the trace found nothing for, naming the id', async () => {
    renderTrace({ modules: [section('jobs', 'Jobs')], rows: [], hidden: 0 })

    const title = await screen.findByText('No object carries this correlation id')
    expect(title.parentElement!.textContent).toContain('ord-7')
    expect(screen.queryByRole('treegrid')).toBeNull()
  })

  it('says an id over 200 characters cannot be traced, with no Retry', async () => {
    const long = 'x'.repeat(201)
    renderTrace(() => {
      throw new DashboardApiError(400, 'Bad Request')
    }, `/trace#${long}`)

    expect(await screen.findByText('This id cannot be traced')).toBeTruthy()
    expect(screen.getByText(/at most 200 characters; this one has 201/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull()
  })

  it('shows any other failed read as an error with Retry, never as an empty trace', async () => {
    const user = userEvent.setup()
    let calls = 0
    const shell = renderTrace(() => {
      calls++

      if (calls === 1) {
        throw new DashboardApiError(500, 'The dashboard answered 500.')
      }

      return jobAndMessage
    })

    expect(await screen.findByText('The dashboard answered 500.', { exact: false })).toBeTruthy()
    expect(screen.queryByText('No object carries this correlation id')).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('treegrid')).toBeTruthy()
    expect(traceRequests(shell.requests)).toHaveLength(2)
  })

  it('reads the trace again when the fragment changes, without loading the page', async () => {
    const shell = renderTrace((request) => ({
      ...jobAndMessage,
      rows: [row('jobs', 'run', (request.body as { correlationId: string }).correlationId, 0)],
    }))

    await screen.findByRole('treegrid', { name: 'Trace of ord-7' })
    await shell.router.navigate('/trace#ord-8')

    expect(await screen.findByRole('treegrid', { name: 'Trace of ord-8' })).toBeTruthy()
    expect(traceRequests(shell.requests).map((request) => request.body)).toEqual([
      { correlationId: 'ord-7' },
      { correlationId: 'ord-8' },
    ])
  })

  it('opens a trace already read at its first row, not at the row focused on the trace before it', async () => {
    const user = userEvent.setup()
    const shell = renderTrace((request) => {
      const id = (request.body as { correlationId: string }).correlationId

      return { ...jobAndMessage, rows: [row('jobs', 'run', `${id}-a`, 0), row('jobs', 'run', `${id}-b`, 0)] }
    })

    await screen.findByRole('treegrid', { name: 'Trace of ord-7' })
    await shell.router.navigate('/trace#ord-8')
    const second = await screen.findByRole('treegrid', { name: 'Trace of ord-8' })
    within(second).getAllByRole('row')[0]!.focus()
    await user.keyboard('{ArrowDown}')
    expect(document.activeElement?.getAttribute('aria-rowindex')).toBe('2')

    // Back to the first trace, which is cached, so it draws at once with no load in between.
    await shell.router.navigate('/trace#ord-7')
    const first = await screen.findByRole('treegrid', { name: 'Trace of ord-7' })

    const tabStop = within(first)
      .getAllByRole('row')
      .find((candidate) => candidate.tabIndex === 0)
    expect(tabStop?.getAttribute('aria-rowindex')).toBe('1')
    expect(traceRequests(shell.requests)).toHaveLength(2)
  })

  it('shows a run with no time yet as waiting on its parent, never as a date', async () => {
    renderTrace({
      ...jobAndMessage,
      rows: [
        row('jobs', 'run', 'r1', 0),
        row('jobs', 'run', 'r2', 1, { status: 'Idle', statusRole: 'sched', at: null, timestampKind: 'due' }),
      ],
    })

    const rows = within(await screen.findByRole('treegrid')).getAllByRole('row')
    expect(rows[1]!.textContent).toContain('Waiting on its parent')
    expect(rows[1]!.querySelector('time')).toBeNull()
  })

  it('takes the fragment exactly as it is, decoded but never trimmed', async () => {
    const shell = renderTrace(jobAndMessage, '/trace#ord%207%20')

    await screen.findByRole('treegrid')
    expect(traceRequests(shell.requests)[0]!.body).toEqual({ correlationId: 'ord 7 ' })
  })

  it('asks nothing for an empty fragment, and says how to open a trace', async () => {
    const shell = renderTrace(jobAndMessage, '/trace')

    expect(await screen.findByText('No correlation id')).toBeTruthy()
    expect(traceRequests(shell.requests)).toEqual([])
  })

  it('Refresh posts the whole trace again', async () => {
    const user = userEvent.setup()
    const shell = renderTrace(jobAndMessage)

    await screen.findByRole('treegrid')
    await user.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(traceRequests(shell.requests)).toHaveLength(2))
  })

  it('moves between rows with the arrow keys and opens the focused row’s object with Enter', async () => {
    const user = userEvent.setup()
    const shell = renderTrace(jobAndMessage)

    const tree = await screen.findByRole('treegrid')
    within(tree).getAllByRole('row')[0]!.focus()
    await user.keyboard('{ArrowDown}{ArrowUp}')
    expect(document.activeElement?.getAttribute('aria-rowindex')).toBe('1')

    await user.keyboard('{Enter}')

    expect(await screen.findByRole('heading', { name: 'Jobs runs view' })).toBeTruthy()
    expect(shell.router.state.location.pathname).toBe('/jobs/runs')
    expect(shell.router.state.location.search).toBe('?selected=r1')
  })

  it.each(['dark', 'light'])('breaks no WCAG A or AA rule axe checks in the %s theme', async (theme) => {
    document.documentElement.setAttribute('data-theme', theme)
    renderTrace({ ...jobAndMessage, hidden: 1 })

    await screen.findByRole('treegrid')
    expect(await axeViolations()).toEqual([])
  })
})
