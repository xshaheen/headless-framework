import { act, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { lock, now, open, summary, user, type FakeServer } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

const locks = [
  lock({ resource: 'orders:rebuild', leaseId: null, ttlMilliseconds: null }),
  lock({ resource: 'reports:daily', ttlMilliseconds: 267_000 }),
]

const fake: FakeServer = { locks }

function lockReads(view: ReturnType<typeof open>) {
  return view.requests.filter((request) => request.path === 'leases/locks')
}

describe('distributed locks', () => {
  it('reads a lock with no expiry and no lease id as No expiry and a dash, with no fencing token column', async () => {
    open(fake, { path: 'locks' })

    const grid = await screen.findByRole('grid', { name: 'Distributed locks' })
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['resource', 'lease id', 'time to live'])
    const row = screen.getByRole('row', { name: /orders:rebuild/ })
    expect(row.textContent).toContain('No expiry')
    expect(row.textContent).toContain('—')
  })

  it('shows the time to live read from the server and never counts it down', async () => {
    open(fake, { path: 'locks' })

    const row = await screen.findByRole('row', { name: /reports:daily/ })
    const shown = row.textContent

    expect(shown).toMatch(/4/)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000)
    })
    expect(screen.getByRole('row', { name: /reports:daily/ }).textContent).toBe(shown)
  })

  it('names every provider scope in the banner, with rows or without', async () => {
    open(fake, { path: 'locks' })

    await screen.findByRole('grid', { name: 'Distributed locks' })
    const text = document.body.textContent ?? ''
    for (const provider of ['Redis', 'PostgreSQL', 'SQL Server', 'in-memory']) {
      expect(text).toContain(provider)
    }
  })

  it('says no locks are held under the banner when the list is empty', async () => {
    open({ locks: [] }, { path: 'locks' })

    expect(await screen.findByText('No locks are held')).toBeTruthy()
    expect(document.body.textContent).toContain('Redis lists every holder')
  })

  it('keeps the rows under the error when a refresh fails', async () => {
    const actor = user()
    let fail = false
    open(
      {
        ...fake,
        extra: (request) => {
          if (fail && request.path === 'leases/locks') {
            throw new DashboardApiError(500, 'The provider did not answer')
          }

          return undefined
        },
      },
      { path: 'locks' },
    )

    await screen.findByRole('row', { name: /orders:rebuild/ })
    fail = true
    await actor.click(screen.getByRole('button', { name: 'Refresh' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toBeTruthy()
    expect(screen.getByRole('button', { name: /Retry/ })).toBeTruthy()
    expect(screen.getByRole('row', { name: /orders:rebuild/ })).toBeTruthy()
  })

  it('names AddHeadlessDistributedLocks and never reads locks on a host without a lock provider', async () => {
    const view = open({ summary: summary({ locks: null }) }, { path: 'locks' })

    expect(await screen.findByText(/AddHeadlessDistributedLocks/)).toBeTruthy()
    expect(lockReads(view)).toHaveLength(0)
  })

  it('explains a lock that is no longer held instead of drawing a blank panel', async () => {
    open({ ...fake, goneLocks: ['orders:rebuild'] }, { path: 'locks?open=orders%3Arebuild' })

    const panel = await screen.findByRole('region', { name: 'Lock orders:rebuild' })
    expect(await within(panel).findByText(/no longer held/)).toBeTruthy()
  })

  it('opens a lock with its fields and no action that unlocks it', async () => {
    const actor = user()
    open(fake, { path: 'locks' })

    await actor.click(await screen.findByRole('row', { name: /reports:daily/ }))
    const panel = await screen.findByRole('region', { name: 'Lock reports:daily' })
    await waitFor(() => expect(panel.textContent).toContain('9f1c2b'))
    expect(within(panel).queryByRole('button', { name: /release|unlock|revoke/i })).toBeNull()
  })
})
