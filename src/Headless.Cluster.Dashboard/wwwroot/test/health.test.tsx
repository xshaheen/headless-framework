import { act, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import type { HealthCheck } from '../src/api.ts'
import { check, checkChange, now, open, result, summary, user, type FakeServer } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

const unprobed: FakeServer = { checks: [check('jobs-scheduler'), check('sandbox-dependency')] }

function described(button: HTMLElement) {
  return document.getElementById(button.getAttribute('aria-describedby')!)?.textContent
}

function reads(view: ReturnType<typeof open>, path: string) {
  return view.requests.filter((request) => request.path === path).length
}

describe('cluster health', () => {
  it('reads every unprobed check as Not probed, says so in the header, and claims no probe interval', async () => {
    open(unprobed, { path: 'health' })

    const grid = await screen.findByRole('grid', { name: 'Health checks' })
    for (const name of ['jobs-scheduler', 'sandbox-dependency']) {
      expect(within(grid).getByRole('row', { name: new RegExp(name) }).textContent).toContain('Not probed')
    }

    await waitFor(() => expect(document.body.textContent).toContain('2 checks · Not probed yet'))
    expect(document.body.textContent).not.toMatch(/every/)
  })

  it('shows Overall as Not probed with no liveness or slowest probe before the first probe', async () => {
    open(unprobed, { path: 'health' })

    const strip = await screen.findByRole('region', { name: 'Health at a glance' })
    expect(strip.textContent).toContain('Not probed')
    expect(strip.textContent).not.toContain('Liveness')
    expect(strip.textContent).not.toContain('Slowest probe')
  })

  it('names the host that answered beside the header count', async () => {
    open(unprobed, { path: 'health' })

    expect(await screen.findByText('Host orders-api-0@14')).toBeTruthy()
  })

  it('probes one check from its panel, reads the health again, and shows the new status in place', async () => {
    const actor = user()
    let checks: HealthCheck[] = [check('jobs-scheduler'), check('sandbox-dependency')]
    const view = open(
      {
        get checks() {
          return checks
        },
        onCommand: (_, ids) => {
          checks = checks.map((item) =>
            item.name === 'sandbox-dependency' ? { ...item, latest: result('Degraded') } : item,
          )

          return { rows: ids.map((id) => ({ id, outcome: 'done', reason: 'Degraded' })) }
        },
      },
      { path: 'health?open=sandbox-dependency' },
    )

    const panel = await screen.findByRole('region', { name: 'Check sandbox-dependency' })
    await actor.click(within(panel).getByRole('button', { name: 'Probe now' }))

    const probe = view.requests.find((request) => request.path === 'cluster/commands/cluster.probe')
    expect(probe?.method).toBe('POST')
    expect(probe?.body).toMatchObject({ ids: ['sandbox-dependency'] })
    await waitFor(() =>
      expect(screen.getByRole('row', { name: /sandbox-dependency/ }).textContent).toContain('Degraded'),
    )
    await waitFor(() => expect(panel.textContent).toContain('Degraded'))
  })

  it('probes every check once with Probe all and announces the result', async () => {
    const actor = user()
    const view = open(
      {
        ...unprobed,
        onCommand: (_, ids) => ({ rows: ids.map((id) => ({ id, outcome: 'done', reason: 'Healthy · 2 checks' })) }),
      },
      { path: 'health' },
    )

    await screen.findByRole('grid', { name: 'Health checks' })
    await actor.click(await screen.findByRole('button', { name: 'Probe all' }))

    await waitFor(() => expect(reads(view, 'cluster/commands/cluster.probe-all')).toBe(1))
    const all = view.requests.find((request) => request.path === 'cluster/commands/cluster.probe-all')
    expect(all?.body).toMatchObject({ ids: ['orders-api-0@14'] })
    await waitFor(() =>
      expect(screen.getAllByRole('status').some((region) => region.textContent?.includes('Healthy · 2 checks'))).toBe(
        true,
      ),
    )
  })

  it('keeps Probe all and Probe now focusable and disabled for a viewer, saying why', async () => {
    open(unprobed, { path: 'health?open=jobs-scheduler', permissions: { canOperate: false } })

    const all = await screen.findByRole('button', { name: 'Probe all' })
    await waitFor(() => expect(described(all)).toBe('Needs operate permission'))
    expect(all.getAttribute('aria-disabled')).toBe('true')
    const panel = await screen.findByRole('region', { name: 'Check jobs-scheduler' })
    const now = within(panel).getByRole('button', { name: 'Probe now' })
    await waitFor(() => expect(described(now)).toBe('Needs operate permission'))
  })

  it('names the monitor remedy and disables both probes when the monitor is missing', async () => {
    const view = open(
      {
        ...unprobed,
        summary: summary({ health: { registered: true, monitor: false } }),
        disabledReason: 'The health check monitor is not registered.',
      },
      { path: 'health' },
    )

    expect(await screen.findByText(/AddHeadlessHealthCheckMonitor\(\)/)).toBeTruthy()
    expect(reads(view, 'cluster/health/checks')).toBe(0)
    const button = await screen.findByRole('button', { name: 'Probe all' })
    await waitFor(() => expect(button.getAttribute('aria-disabled')).toBe('true'))
  })

  it('names AddHealthChecks and reads no checks on a host without health checks', async () => {
    const view = open({ ...unprobed, summary: summary({ health: null }) }, { path: 'health' })

    expect(await screen.findByText('Health checks are not registered')).toBeTruthy()
    expect(document.body.textContent).toContain('AddHealthChecks()')
    expect(reads(view, 'cluster/health/checks')).toBe(0)
  })

  it('reads only the checks in the status the URL filters by', async () => {
    const view = open({ checks: [check('jobs', { latest: result('Unhealthy') })] }, { path: 'health?status=Unhealthy' })

    await screen.findByRole('row', { name: /jobs/ })
    expect(
      view.requests.some(
        (request) => request.path === 'cluster/health/checks' && request.query?.status === 'Unhealthy',
      ),
    ).toBe(true)
  })

  it('shows a check’s recent results with a summary of how its status ran', async () => {
    const actor = user()
    const history = [
      ...Array.from({ length: 8 }, (_, index) =>
        result('Degraded', { completedAt: `2026-11-27T09:5${9 - index}:00Z` }),
      ),
      ...Array.from({ length: 22 }, (_, index) =>
        result('Healthy', { completedAt: `2026-11-27T09:${String(29 - index).padStart(2, '0')}:00Z` }),
      ),
    ]
    open(
      { checks: [check('jobs-scheduler', { latest: history[0]! })], history: { 'jobs-scheduler': history } },
      { path: 'health?open=jobs-scheduler' },
    )

    const panel = await screen.findByRole('region', { name: 'Check jobs-scheduler' })
    await actor.click(within(panel).getByRole('tab', { name: 'History' }))
    expect(panel.textContent).toContain('Healthy for 22 probes, then Degraded for the last 8')
  })

  it('shows a refused probe in place', async () => {
    const actor = user()
    open(
      {
        ...unprobed,
        onCommand: () => {
          throw new DashboardApiError(409, 'The health check monitor is not registered.')
        },
      },
      { path: 'health?open=jobs-scheduler' },
    )

    const panel = await screen.findByRole('region', { name: 'Check jobs-scheduler' })
    await actor.click(within(panel).getByRole('button', { name: 'Probe now' }))

    await waitFor(() => expect(document.body.textContent).toContain('The health check monitor is not registered.'))
  })

  it('reads the checks again when a probed check is pushed on the stream', async () => {
    const view = open(unprobed, { path: 'health' })
    await screen.findByRole('grid', { name: 'Health checks' })
    const before = reads(view, 'cluster/health/checks')

    act(() => view.stream.push('change', checkChange('sandbox-dependency')))

    await waitFor(() => expect(reads(view, 'cluster/health/checks')).toBe(before + 1))
  })

  it('shows Stale and keeps the rows while the stream is down', async () => {
    const view = open({ checks: [check('jobs', { latest: result('Healthy') })] }, { path: 'health' })
    await screen.findByRole('row', { name: /jobs/ })

    act(() => view.stream.setStatus('stale'))

    expect(await screen.findByText(/Stale/)).toBeTruthy()
    expect(screen.getByRole('row', { name: /jobs/ })).toBeTruthy()
  })
})
