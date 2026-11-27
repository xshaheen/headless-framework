import { screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { lease, now, open, summary, user, type FakeServer } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

const progress = lease({
  resource: 'export/2026-11-03',
  holder: null,
  progressContract: 'export-cursor/v1',
  progressSizeBytes: 1229,
})

const leases = [
  progress,
  lease({ resource: 'export/2026-11-04', tenantId: null }),
  lease({ resource: 'invoice/42', kind: 'invoice-batch', tenantId: 'globex' }),
  lease({ resource: 'stuck/1', status: 'Expired', state: 'Expired', takeoverCount: 2 }),
  lease({ resource: 'stuck/2', status: 'Expired', state: 'Expired' }),
  lease({ resource: 'done/1', status: 'Settled', state: 'Ended', endedAt: '2026-11-27T09:59:00Z' }),
  lease({ resource: 'done/2', status: 'Released', state: 'Ended' }),
  lease({ resource: 'done/3', status: 'Revoked', state: 'Ended' }),
  lease({ resource: 'done/4', status: 'Abandoned', state: 'Ended' }),
  lease({ resource: 'done/5', status: 'Settled', state: 'Ended' }),
]

const fake: FakeServer = { leases }

function fencedReads(view: ReturnType<typeof open>) {
  return view.requests.filter((request) => request.path === 'leases/fenced')
}

describe('fenced leases', () => {
  it('lists every lease with the served columns and the stored status as its word', async () => {
    open(fake)

    const grid = await screen.findByRole('grid', { name: 'Fenced leases' })
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['resource', 'state', 'kind', 'tenant', 'generation', 'granted', 'expires', 'takeovers', 'progress'])
    expect(within(grid).getAllByRole('row')).toHaveLength(11)
    expect(screen.getByRole('row', { name: /stuck\/1/ }).textContent).toContain('Expired')
    expect(screen.getByRole('row', { name: /done\/1/ }).textContent).toContain('Settled')
    expect(screen.getByRole('row', { name: /done\/3/ }).textContent).toContain('Revoked')
    expect(screen.getByRole('row', { name: /export\/2026-11-04/ }).textContent).toContain('Host')
    expect(document.body.textContent).not.toMatch(/24 h/)
  })

  it('reads a Live row as Live even when its expiry has passed on the browser clock', async () => {
    open({ leases: [lease({ resource: 'late/1', expiresAt: '2026-11-27T09:00:00Z' })] })

    expect((await screen.findByRole('row', { name: /late\/1/ })).textContent).toContain('Live')
  })

  it('sends the state, kind, and prefix filters to the server', async () => {
    const actor = user()
    const view = open(fake)

    await screen.findByRole('grid', { name: 'Fenced leases' })
    await actor.click(screen.getByRole('button', { name: /^Expired/ }))
    await waitFor(() => expect(fencedReads(view).some((request) => request.query?.state === 'Expired')).toBe(true))

    await actor.type(screen.getByRole('searchbox', { name: 'Resource prefix' }), 'stuck/')
    await waitFor(() =>
      expect(
        fencedReads(view).some((request) => request.query?.prefix === 'stuck/' && request.query.after === undefined),
      ).toBe(true),
    )
  })

  it('keeps the kind, tenant, and prefix filters when a state card is followed', async () => {
    const actor = user()
    const view = open(fake, { path: '?kind=ledger-export&prefix=export%2F' })

    await screen.findByRole('grid', { name: 'Fenced leases' })
    await actor.click(screen.getByRole('link', { name: /Expired/ }))
    await waitFor(() =>
      expect(
        fencedReads(view).some(
          (request) =>
            request.query?.state === 'Expired' &&
            request.query.kind === 'ledger-export' &&
            request.query.prefix === 'export/',
        ),
      ).toBe(true),
    )
  })

  it('reads the kind filter from the URL', async () => {
    const view = open(fake, { path: '?kind=invoice-batch' })

    await screen.findByRole('row', { name: /invoice\/42/ })
    expect(fencedReads(view).every((request) => request.query?.kind === 'invoice-batch')).toBe(true)
  })

  it('sends scope=host for the host scope', async () => {
    const view = open(fake, { path: '?scope=host' })

    await screen.findByRole('grid', { name: 'Fenced leases' })
    expect(fencedReads(view).some((request) => request.query?.scope === 'host')).toBe(true)
  })

  it('sends no tenant until the id field holds one, then sends it once Enter commits it', async () => {
    const actor = user()
    const view = open(fake, { path: '?tenant=' })

    const field = await screen.findByRole('searchbox', { name: 'Tenant id' })
    expect(fencedReads(view).every((request) => request.query?.tenant === undefined)).toBe(true)
    expect(screen.queryByRole('alert')).toBeNull()

    await actor.type(field, 'acme')
    expect(fencedReads(view).some((request) => request.query?.tenant === 'acme')).toBe(false)
    await actor.keyboard('{Enter}')
    await waitFor(() => expect(fencedReads(view).some((request) => request.query?.tenant === 'acme')).toBe(true))
  })

  it('shows a capped count as N+ in the stat strip and the state segment', async () => {
    const capped = { value: 10_000, capped: true }
    open({
      ...fake,
      counts: { live: capped, expired: { value: 0, capped: false }, ended: { value: 0, capped: false }, total: capped },
    })

    const strip = await screen.findByRole('region', { name: 'Leases at a glance' })
    await waitFor(() => expect(within(strip).getByRole('link', { name: /Live/ }).textContent).toContain('10,000+'))
    expect(screen.getByRole('button', { name: /^Live/ }).textContent).toContain('10,000+')
  })

  it('asks for the next page with the cursor the first page returned', async () => {
    const view = open({ ...fake, leaseNext: 'v1.next' })

    await screen.findByRole('grid', { name: 'Fenced leases' })
    // jsdom lays nothing out, so focusing the last row stands in for scrolling to the end.
    await user().click(screen.getByRole('row', { name: /done\/5/ }))
    await waitFor(() => expect(fencedReads(view).some((request) => request.query?.after === 'v1.next')).toBe(true))
  })

  it('opens a lease with its size and contract, never its bytes, and why there is no Release action', async () => {
    const actor = user()
    open(fake)

    await actor.click(await screen.findByRole('row', { name: /export\/2026-11-03/ }))
    const panel = await screen.findByRole('region', { name: 'Lease export/2026-11-03' })

    await waitFor(() => expect(panel.textContent).toContain('1.2 kB · export-cursor/v1'))
    expect(panel.textContent).toContain('No holder recorded')
    expect(panel.textContent).toContain('Why there is no Release action')
    expect(within(panel).queryByRole('button', { name: /release|revoke|purge/i })).toBeNull()
  })

  it('explains a purged lease instead of drawing a blank panel', async () => {
    open({ ...fake, goneLeases: [progress.id] }, { path: `?open=${encodeURIComponent(progress.id)}` })

    const panel = await screen.findByRole('region', { name: `Lease ${progress.id}` })
    expect(await within(panel).findByText(/has no row any more/)).toBeTruthy()
  })

  it('refetches the list, the counts, and the open panel once each on Refresh', async () => {
    const actor = user()
    const view = open(fake, { path: `?open=${encodeURIComponent(progress.id)}` })

    await screen.findByRole('region', { name: 'Lease export/2026-11-03' })
    await screen.findByRole('grid', { name: 'Fenced leases' })
    const before = (path: string) => view.requests.filter((request) => request.path === path).length
    const list = before('leases/fenced')
    const counts = before('leases/fenced/counts')
    const panel = before('leases/fenced/lease')

    await actor.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(before('leases/fenced/lease')).toBe(panel + 1))
    expect(before('leases/fenced')).toBe(list + 1)
    expect(before('leases/fenced/counts')).toBe(counts + 1)
  })

  it('offers Clear filters when a filter matches nothing', async () => {
    open({ leases: [] }, { path: '?kind=ledger-export' })

    expect(await screen.findByText('No leases match')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeTruthy()
  })

  it('says how a lease appears when the store holds none', async () => {
    open({ leases: [] })

    expect(await screen.findByText(/IFencedLeases.GrantAsync/)).toBeTruthy()
  })

  it('shows a failed first page as an error with Retry, not an empty table', async () => {
    open({
      ...fake,
      extra: (request) => {
        if (request.path === 'leases/fenced') {
          throw new DashboardApiError(500, 'The store did not answer')
        }

        return undefined
      },
    })

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('The store did not answer')
    expect(screen.getByRole('button', { name: /Retry/ })).toBeTruthy()
    expect(screen.queryByRole('grid', { name: 'Fenced leases' })).toBeNull()
  })

  it('keeps the rows under the error when a refresh fails', async () => {
    const actor = user()
    let fail = false
    open({
      ...fake,
      extra: (request) => {
        if (fail && request.path === 'leases/fenced') {
          throw new DashboardApiError(500, 'The store did not answer')
        }

        return undefined
      },
    })

    await screen.findByRole('row', { name: /stuck\/1/ })
    fail = true
    await actor.click(screen.getByRole('button', { name: 'Refresh' }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByRole('button', { name: /Retry/ })).toBeTruthy()
    expect(screen.getByRole('row', { name: /stuck\/1/ })).toBeTruthy()
  })

  it('sends a lock-only host to the locks view and never reads fenced leases', async () => {
    const view = open({ summary: summary({ fencing: null }), locks: [] })

    expect(await screen.findByRole('heading', { name: 'Distributed locks', level: 1 })).toBeTruthy()
    expect(fencedReads(view)).toHaveLength(0)
  })

  it('names AddHeadlessFencing when the fenced view is reached on a host without Fencing', async () => {
    const view = open({ summary: summary({ fencing: null, locks: null }) })

    expect(await screen.findByText(/AddHeadlessFencing/)).toBeTruthy()
    expect(fencedReads(view)).toHaveLength(0)
  })

  it('shows a viewer the same read-only page', async () => {
    open(fake, { permissions: { canView: true, canOperate: false } })

    await screen.findByRole('grid', { name: 'Fenced leases' })
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeTruthy()
  })
})
