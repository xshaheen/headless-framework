import { Button } from '@headless/dashboard-sdk/components'
import { useOperations, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import { DashboardApiError, listKey, useDashboardList, type ApiRequest } from '@headless/dashboard-sdk/data'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { BootstrapState } from '../src/bootstrap.ts'
import { chunkUrl, moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { emptyAggregates, renderShell, type ApiHandler } from './harness.tsx'

const requeue: CommandDefinition = {
  id: 'requeue',
  label: 'Requeue',
  objectKind: 'run',
  permission: 'operate',
  confirmation: 'confirm',
  typedConfirmationAbove: 100,
  requiresReason: true,
  supportsFilter: true,
  arguments: { type: 'object', required: [], properties: {} },
  canRun: true,
}

function StartOperation() {
  const { start } = useOperations()

  return (
    <Button onPress={() => start({ module: 'diag', command: requeue, filter: { status: 'failed' }, noun: 'failed runs' })}>
      Requeue all failed runs
    </Button>
  )
}

/** Answers matchMedia as a window `width` pixels wide would. */
function atWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: width <= Number(/max-width:\s*(\d+)px/.exec(query)?.[1] ?? Infinity),
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }))
}

/** Jobs and Platform as two products, in the dashboard's product order, with the Jobs sidebar's state counts. */
function twoProducts(): BootstrapState {
  const jobs = moduleManifest('jobs', { displayName: 'Jobs', objectKinds: { run: {} } })
  const audit = moduleManifest('audit', { displayName: 'Audit' })

  return {
    ...shellState([jobs, audit]),
    products: [
      {
        id: 'jobs',
        label: 'Jobs',
        modules: ['jobs'],
        sections: [
          {
            module: 'jobs',
            label: 'States',
            entries: [
              { id: 'jobs.nav.failed', label: 'Failed', route: 'runs?status=Failed', state: 'fail', countKey: 'Failed' },
              { id: 'jobs.nav.queued', label: 'Queued', route: 'runs?status=Queued', state: 'enq', countKey: 'Queued' },
              { id: 'jobs.nav.runs', label: 'All runs', route: 'runs', icon: 'runs' },
            ],
          },
        ],
      },
      { id: 'platform', label: 'Platform', modules: ['audit'], sections: [{ module: 'audit', entries: [] }] },
    ],
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  vi.useRealTimers()
  localStorage.clear()
})

describe('shell layout', () => {
  it('keeps the Operation running across navigation, with Stop in view and the leave warning armed', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    const files = { ...moduleFiles(modules), [chunkUrl('diag', 'runs')]: { default: StartOperation } }
    let release: () => void = () => undefined
    let chunks = 0

    const shell = renderShell(shellState(modules), {
      path: '/diag/runs',
      files,
      api: async (request) => {
        if (request.path.endsWith('/confirm')) {
          return { operationId: 'op1', count: 3, confirmation: 'confirm', sample: [], token: 't', expiresAt: '', chunkSize: 1 }
        }

        if (request.path.endsWith('/chunks')) {
          chunks++

          if (chunks === 2) {
            // Hold the second chunk until the test has navigated away.
            await new Promise<void>((resolve) => {
              release = resolve
            })
          }

          return { operationId: 'op1', rows: [{ id: `r${chunks}`, outcome: 'done' }], nextCursor: chunks < 3 ? `c${chunks}` : null, done: chunks >= 3 }
        }

        return emptyAggregates(request)
      },
    })

    await user.click(await screen.findByRole('button', { name: 'Requeue all failed runs' }))
    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'card processor recovered')
    await user.click(screen.getByRole('button', { name: 'Count rows' }))
    await user.click(await screen.findByRole('button', { name: 'Requeue 3 failed runs' }))

    const panel = await screen.findByRole('group', { name: 'Requeue failed runs' })
    await waitFor(() => expect(chunks).toBe(2))

    const unload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(unload)
    expect(unload.defaultPrevented).toBe(true)

    await user.click(within(screen.getByRole('navigation', { name: 'Jobs' })).getByRole('link', { name: 'Settings' }))
    expect(await screen.findByRole('heading', { name: 'Diagnostics settings view' })).toBeTruthy()
    expect(within(panel).getByRole('button', { name: 'Stop' })).toBeTruthy()

    release()
    await waitFor(() => expect(chunks).toBe(3))
    expect(await within(screen.getByRole('group', { name: /Requeue failed runs/ })).findByText(/Completed/)).toBeTruthy()
    expect(shell.router.state.location.pathname).toBe('/diag/settings')
  })

  it('shows one tab per registered product, in the product order, the current one marked', async () => {
    renderShell(twoProducts(), { path: '/audit/runs', files: moduleFiles(twoProducts().modules) })

    const tabs = within(await screen.findByRole('navigation', { name: 'Products' })).getAllByRole('link')
    await waitFor(() =>
      expect(tabs.map((tab) => tab.textContent)).toEqual(['JobsJobs, nothing needs attention', 'PlatformPlatform, nothing needs attention']),
    )
    expect(tabs[1]!.getAttribute('aria-current')).toBe('page')
    expect(tabs[0]!.getAttribute('aria-current')).toBeNull()
  })

  it('marks a product tab by its worst attention, in words as well as the dot', async () => {
    const api: ApiHandler = (request) =>
      request.path === 'needs-you'
        ? {
            modules: [
              {
                id: 'jobs',
                displayName: 'Jobs',
                stale: false,
                items: [{ id: 'f', groupKey: 'f', title: 'CardException: declined', severity: 'critical', count: 23, capped: false }],
              },
            ],
          }
        : emptyAggregates(request)
    renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    const jobs = await screen.findByRole('link', { name: 'Jobs, 23 need attention' })
    expect(jobs.className).toContain('hd-tone-fail')
    expect(await screen.findByRole('button', { name: 'Attention 23' })).toBeTruthy()
  })

  it('shows a capped count as N+, and the current entry by aria-current', async () => {
    const api: ApiHandler = (request) =>
      request.path === 'counts'
        ? { product: 'jobs', counts: { Failed: { count: 10000, capped: true }, Queued: { count: 12, capped: false } }, stale: [] }
        : emptyAggregates(request)
    renderShell(twoProducts(), { path: '/jobs/runs?status=Failed', files: moduleFiles(twoProducts().modules), api })

    const sidebar = await screen.findByRole('navigation', { name: 'Jobs' })
    const failed = within(sidebar).getByRole('link', { name: /Failed/ })
    await waitFor(() => expect(failed.textContent).toBe('Failed10,000+'))
    expect(within(sidebar).getByRole('link', { name: /Queued/ }).textContent).toBe('Queued12')
    expect(failed.getAttribute('aria-current')).toBe('page')
    expect(within(sidebar).getByRole('link', { name: /All runs/ }).getAttribute('aria-current')).toBeNull()
  })

  it('shows a count that some sources did not answer as N+, naming them in its accessible name', async () => {
    const api: ApiHandler = (request) =>
      request.path === 'counts'
        ? {
            product: 'jobs',
            counts: { Failed: { count: 48120, capped: false, unavailable: ['outbox-2'] }, Queued: { count: 12, capped: false } },
            stale: [],
          }
        : emptyAggregates(request)
    renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    const sidebar = await screen.findByRole('navigation', { name: 'Jobs' })
    const failed = await within(sidebar).findByRole('link', { name: /^Failed\s?48,120\+ · outbox-2 unavailable$/ })
    expect(failed.querySelector('.hd-side-count > [aria-hidden="true"]')?.textContent).toBe('48,120+')
    expect(within(sidebar).getByRole('link', { name: /Queued/ }).textContent).toBe('Queued12')
    expect(within(sidebar).queryByRole('status')?.textContent ?? '').toBe('')
  })

  it('shows an en dash in each count when the counts request fails, and announces it once', async () => {
    const api: ApiHandler = (request) => {
      if (request.path === 'counts') {
        throw new DashboardApiError(503, 'Unavailable')
      }

      return emptyAggregates(request)
    }
    renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    const sidebar = await screen.findByRole('navigation', { name: 'Jobs' })
    await waitFor(() => expect(within(sidebar).getByRole('status').textContent).toBe('Jobs counts did not load.'))
    expect(within(sidebar).getByRole('link', { name: /Failed/ }).textContent).toBe('Failed–')
    expect(within(sidebar).getByRole('link', { name: /Queued/ }).textContent).toBe('Queued–')
  })

  it('refetches the counts on a change of the product’s topics, at most once every 5 s', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const shell = renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules) })
    const counted = () => shell.requests.filter((request) => request.path === 'counts').length

    await waitFor(() => expect(counted()).toBe(1))
    expect(shell.stream.topics()).toContain('jobs.run')

    for (let index = 0; index < 3; index++) {
      shell.stream.push('change', { module: 'jobs', kind: 'run', id: `r${index}`, topic: 'jobs.run', version: '1' })
    }

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
    })
    await waitFor(() => expect(counted()).toBe(2))
  })

  it('holds run changes while paused and shows how many wait, while the operator’s own command still updates its row', async () => {
    const server = [
      { id: 'r1', version: '1', status: 'Failed' },
      { id: 'r2', version: '1', status: 'Failed' },
      { id: 'r3', version: '1', status: 'Failed' },
    ]

    function Runs() {
      const client = useQueryClient()
      const list = useDashboardList({ topic: 'jobs.run', fetch: async () => ({ items: server.map((row) => ({ ...row })) }) })

      return (
        <>
          <ul aria-label="Runs">
            {list.data?.items.map((row) => (
              <li key={row.id}>{`${row.id} ${row.status}`}</li>
            ))}
          </ul>
          <Button
            onPress={() => {
              // A command's result comes back through the API; the view refetches, as the Jobs views do after one.
              server[0]!.status = 'Queued'
              void client.invalidateQueries({ queryKey: listKey('jobs.run') })
            }}
          >
            Requeue r1
          </Button>
        </>
      )
    }

    const modules = [moduleManifest('jobs')]
    const shell = renderShell(shellState(modules), {
      path: '/jobs/runs',
      files: { ...moduleFiles(modules), [chunkUrl('jobs', 'runs')]: { default: Runs } },
    })
    const user = userEvent.setup()
    const rows = async () => (await screen.findAllByRole('listitem')).map((row) => row.textContent)

    expect(await rows()).toEqual(['r1 Failed', 'r2 Failed', 'r3 Failed'])
    await user.click(screen.getByRole('button', { name: 'Pause live updates' }))

    for (const id of ['r1', 'r2', 'r3']) {
      shell.stream.push('change', { module: 'jobs', kind: 'run', id, topic: 'jobs.run', version: '2', projection: { status: 'Succeeded' } })
    }

    expect(screen.getByText('Paused · 3 updates')).toBeTruthy()
    expect(await rows()).toEqual(['r1 Failed', 'r2 Failed', 'r3 Failed'])

    await user.click(screen.getByRole('button', { name: 'Requeue r1' }))
    await waitFor(async () => expect(await rows()).toEqual(['r1 Queued', 'r2 Failed', 'r3 Failed']))

    await user.click(screen.getByRole('button', { name: 'Resume live updates' }))

    expect(await rows()).toEqual(['r1 Succeeded', 'r2 Succeeded', 'r3 Succeeded'])
    expect(screen.queryByText(/Paused/)).toBeNull()
  })

  it('applies a session end at once while paused', async () => {
    const user = userEvent.setup()
    const ended = vi.fn()
    const { withSessionEnd } = await import('../src/app.tsx')
    // Resolve answers 401, as it would once the cookie expired; everything else answers as usual.
    const api = withSessionEnd(
      {
        request: async <T,>(request: ApiRequest) => {
          if (request.path === 'resolve') {
            throw new DashboardApiError(401, 'Unauthorized')
          }

          return (await emptyAggregates(request)) as T
        },
      },
      ended,
    )
    const modules = [moduleManifest('jobs')]
    renderShell(shellState(modules), { path: '/jobs/runs', files: moduleFiles(modules), api: (request) => api.request(request) })

    await user.click(await screen.findByRole('button', { name: 'Pause live updates' }))
    await user.click(screen.getByRole('textbox', { name: 'Go to id' }))
    await user.paste('run-401')

    await waitFor(() => expect(ended).toHaveBeenCalledOnce())
    expect(screen.getByRole('button', { name: 'Resume live updates' })).toBeTruthy()
  })

  it('reads a stale module’s counts as en dashes, and announces it once', async () => {
    const api: ApiHandler = (request) =>
      request.path === 'counts' ? { product: 'jobs', counts: {}, stale: [{ id: 'jobs', displayName: 'Jobs' }] } : emptyAggregates(request)
    renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    const sidebar = await screen.findByRole('navigation', { name: 'Jobs' })
    await waitFor(() => expect(within(sidebar).getByRole('status').textContent).toBe('Jobs counts did not load.'))
    expect(within(sidebar).getByRole('link', { name: /Failed/ }).textContent).toBe('Failed–')
  })

  it('owes a counts refetch that falls due while the tab is hidden, and pays it when the tab shows', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let visibility: DocumentVisibilityState = 'visible'
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
    const shell = renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules) })
    const counted = () => shell.requests.filter((request) => request.path === 'counts').length

    await waitFor(() => expect(counted()).toBe(1))
    visibility = 'hidden'
    shell.stream.push('change', { module: 'jobs', kind: 'run', id: 'r1', topic: 'jobs.run', version: '1' })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
    })
    expect(counted()).toBe(1)

    visibility = 'visible'
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'))
    })
    await waitFor(() => expect(counted()).toBe(2))
  })

  it('holds a counts refetch armed before Pause until Resume', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const shell = renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules) })
    const counted = () => shell.requests.filter((request) => request.path === 'counts').length

    await waitFor(() => expect(counted()).toBe(1))
    shell.stream.push('change', { module: 'jobs', kind: 'run', id: 'r1', topic: 'jobs.run', version: '1' })
    await user.click(screen.getByRole('button', { name: 'Pause live updates' }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
    })
    expect(counted()).toBe(1)

    await user.click(screen.getByRole('button', { name: 'Resume live updates' }))
    await waitFor(() => expect(counted()).toBe(2))
  })

  it('refetches the counts on a stream reset, a topic that became live, and a return to the product', async () => {
    const shell = renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules) })
    const counted = () => shell.requests.filter((request) => request.path === 'counts').length

    await waitFor(() => expect(counted()).toBe(1))
    act(() => shell.stream.push('reset', {}))
    await waitFor(() => expect(counted()).toBe(2))

    act(() => shell.stream.push('subscribed', { topics: ['jobs.run'], added: ['jobs.run'] }))
    await waitFor(() => expect(counted()).toBe(3))

    await act(() => shell.router.navigate('/audit/runs'))
    await act(() => shell.router.navigate('/jobs/runs'))
    await waitFor(() => expect(counted()).toBe(4))
  })

  it('reads Attention as unknown, not all clear, while it loads, after it fails, and for a module that did not answer', async () => {
    let answer: () => unknown = () => new Promise(() => undefined)
    const api: ApiHandler = (request) => (request.path === 'needs-you' ? answer() : emptyAggregates(request))
    const first = renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    expect((await screen.findByRole('button', { name: 'Attention' })).className).toContain('hd-tone-off')
    expect(screen.getByRole('link', { name: 'Jobs, attention not loaded' })).toBeTruthy()
    first.unmount()

    answer = () => {
      throw new DashboardApiError(503, 'Unavailable')
    }
    const second = renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    expect(await screen.findByRole('link', { name: 'Jobs, attention not loaded' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Attention' }).className).toContain('hd-tone-off')
    second.unmount()

    answer = () => ({ modules: [{ id: 'jobs', displayName: 'Jobs', stale: true, items: [] }] })
    renderShell(twoProducts(), { path: '/jobs/runs', files: moduleFiles(twoProducts().modules), api })

    const jobs = await screen.findByRole('link', { name: 'Jobs did not answer' })
    expect(jobs.className).toContain('hd-tone-off')
    expect(screen.getByRole('button', { name: 'Attention' }).className).toContain('hd-tone-off')
    expect(screen.getByRole('link', { name: 'Platform, nothing needs attention' }).className).toContain('hd-tone-ok')
  })

  it('keeps the clock out of any live region, and announces going stale once', async () => {
    const modules = [moduleManifest('jobs')]
    const shell = renderShell(shellState(modules), { path: '/jobs/runs', files: moduleFiles(modules) })

    const clock = (await screen.findByRole('banner')).querySelector('time.hd-clock')!
    expect(clock.closest('[aria-live], [role="status"]')).toBeNull()

    shell.stream.setStatus('stale')

    const announced = screen
      .getAllByRole('status')
      .map((region) => region.textContent)
      .filter((text) => text === 'Live updates stopped. What you see may be out of date.')
    expect(announced).toHaveLength(1)
  })

  it('folds the zone and clock into the account menu at 1024 px', async () => {
    atWidth(1024)
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    renderShell(shellState(modules), { path: '/jobs/runs', files: moduleFiles(modules) })

    const bar = await screen.findByRole('banner')
    expect(within(bar).queryByRole('button', { name: /Display time zone/ })).toBeNull()
    expect(bar.querySelector('time.hd-clock')).toBeNull()
    expect(within(bar).getByRole('button', { name: 'Pause live updates' })).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Account: Mona Haddad' }))

    const menu = screen.getByRole('dialog', { name: 'Account' })
    expect(within(menu).getByRole('combobox', { name: 'Time zone' })).toBeTruthy()
    expect(menu.querySelector('time.hd-clock')).toBeTruthy()
  })

  it('closes the sidebar behind a Menu button at 400 px', async () => {
    atWidth(400)
    const user = userEvent.setup()
    const modules = [moduleManifest('jobs')]
    renderShell(shellState(modules), { path: '/jobs/runs', files: moduleFiles(modules) })

    const menu = await screen.findByRole('button', { name: 'Menu' })
    expect(screen.queryByRole('navigation', { name: 'Jobs' })).toBeNull()
    expect(menu.getAttribute('aria-expanded')).toBe('false')

    await user.click(menu)
    await user.click(within(screen.getByRole('navigation', { name: 'Jobs' })).getByRole('link', { name: 'Settings' }))

    expect(await screen.findByRole('heading', { name: 'Module jobs settings view' })).toBeTruthy()
    expect(screen.queryByRole('navigation', { name: 'Jobs' })).toBeNull()
  })

  it('shows Sign-out failed beside Sign out in the account menu, and the operator stays signed in', async () => {
    const user = userEvent.setup()
    renderShell(shellState([]), {
      signOut: async () => {
        throw new Error('503')
      },
    })

    await user.click(await screen.findByRole('button', { name: 'Account: Mona Haddad' }))
    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    expect((await screen.findByRole('alert')).textContent).toContain('Sign-out failed')
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy()
    expect(screen.getByText('Mona Haddad')).toBeTruthy()
  })

  it('shows a viewer operate entries disabled with Needs operate permission, and View only', async () => {
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics', canOperate: false })]
    renderShell(shellState(modules), { files: moduleFiles(modules), path: '/diag/runs' })

    const sidebar = await screen.findByRole('navigation', { name: 'Jobs' })
    const settings = within(sidebar).getByRole('link', { name: /Settings/ })
    expect(settings.getAttribute('aria-disabled')).toBe('true')
    expect(settings.getAttribute('href')).toBeNull()
    expect(settings.getAttribute('aria-describedby')).toBeTruthy()
    expect(document.getElementById(settings.getAttribute('aria-describedby')!)?.textContent).toBe('Needs operate permission')
    expect(screen.getByText('View only')).toBeTruthy()
  })

  it('keeps the zone chosen in the top bar after a reload', async () => {
    const user = userEvent.setup()
    const first = renderShell(shellState([]))

    await user.click(await screen.findByRole('button', { name: 'Display time zone: UTC' }))
    await user.selectOptions(screen.getByLabelText('Time zone'), 'Africa/Cairo')
    expect(screen.getByRole('button', { name: 'Display time zone: Africa/Cairo' })).toBeTruthy()

    first.unmount()
    renderShell(shellState([]))

    expect(await screen.findByRole('button', { name: 'Display time zone: Africa/Cairo' })).toBeTruthy()
  })
})
