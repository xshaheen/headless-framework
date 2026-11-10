import { screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { node, now, open, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

describe('jobs servers', () => {
  const nodes = [
    node({
      identity: 'node-a',
      role: 'api',
      claimedFunctions: ['billing.charge', 'billing.refund'],
      isLocal: true,
      maxConcurrency: 16,
      running: 2,
      lastHeartbeatAt: '2026-11-05T09:59:50Z',
      lastHostException: 'System.TimeoutException: The store did not answer\n   at Scheduler.Poll()',
      lastHostExceptionAt: '2026-11-05T09:58:00Z',
    }),
    node({
      identity: 'node-b',
      state: 'Suspected',
      claimedFunctions: null,
      maxConcurrency: null,
      running: 500,
      runningCapped: true,
      lastHeartbeatAt: null,
    }),
  ]

  it('lists each server’s role, state, concurrency, running count, last beat, and claimed functions', async () => {
    open({ nodes }, { path: 'nodes' })

    const row = await screen.findByRole('row', { name: /node-a/ })
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Server',
      'Role',
      'State',
      'Max concurrency',
      'Running',
      'Last beat',
      'Claimed functions',
    ])
    const cells = [...row.children].map((cell) => cell.textContent)
    expect(cells).toEqual(['node-aThis node', 'api', 'Alive', '16', '2', '10 sec. ago', 'billing.chargebilling.refund'])

    const peer = screen.getByRole('row', { name: /node-b/ })
    expect([...peer.children].map((cell) => cell.textContent)).toEqual([
      'node-b',
      '',
      'Suspected',
      'Not published',
      '500+',
      'No heartbeat',
      'No run filter published',
    ])
  })

  it('alerts the serving node’s last host exception on the page', async () => {
    open({ nodes }, { path: 'nodes' })

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe(
      "This node's scheduler last failed: System.TimeoutException: The store did not answer",
    )
  })

  it('shows no host-exception alert when the serving node reported none', async () => {
    open({ nodes: [node({ isLocal: true })] }, { path: 'nodes' })

    await screen.findByRole('row', { name: /node-a/ })
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows the serving node’s host exception in its panel, and a peer’s as known only on that node', async () => {
    const actor = user()
    open({ nodes }, { path: 'nodes' })

    await actor.click(await screen.findByRole('row', { name: /node-a/ }))
    const panel = await screen.findByRole('region', { name: 'Server node-a' })
    expect(panel.textContent).toContain('Serves this pageThis node')
    expect(panel.textContent).toContain('System.TimeoutException: The store did not answer')
    expect(within(panel).getByRole('link', { name: 'billing.charge' }).getAttribute('href')).toBe(
      '/jobs/functions/billing.charge',
    )
    expect(within(panel).queryByRole('button', { name: /scheduler/i })).toBeNull()

    await actor.click(screen.getByRole('row', { name: /node-b/ }))
    const peer = await screen.findByRole('region', { name: 'Server node-b' })
    expect(within(peer).getByText('Known only on that node')).toBeTruthy()
    expect(peer.textContent).not.toContain('TimeoutException')
  })
})
