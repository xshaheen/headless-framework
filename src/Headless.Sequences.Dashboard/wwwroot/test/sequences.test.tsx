import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  acmeInvoice,
  acmeInvoiceLastYear,
  counter,
  globexOrder,
  hostTicket,
  invoicePolicy,
  now,
  open,
  summaryOf,
  user,
  type FakeServer,
} from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

const rows = [acmeInvoice, acmeInvoiceLastYear, globexOrder, hostTicket]
const fake: FakeServer = { counters: rows }

function counterRequests(view: ReturnType<typeof open>) {
  return requestsTo(view, 'sequences/counters')
}

function requestsTo(view: ReturnType<typeof open>, path: string) {
  return view.requests.filter((request) => request.path === path)
}

function moduleRequests(view: ReturnType<typeof open>) {
  return view.requests.filter((request) => request.path.startsWith('sequences/'))
}

/** The query of the link a stat card leads to, as the browser would follow it. */
function statLink(label: string) {
  const link = within(screen.getByRole('region', { name: 'Sequences at a glance' })).getByRole('link', {
    name: new RegExp(`^${label}`),
  })

  return new URL(link.getAttribute('href') ?? '', 'http://dashboard.test').searchParams
}

/** A fake whose one path fails, so a single read can fail while the rest of the page answers. */
function failing(path: string, base: FakeServer = fake): FakeServer & { isFailing: boolean } {
  const state: FakeServer & { isFailing: boolean } = {
    ...base,
    isFailing: false,
    extra: (request) => {
      if (state.isFailing && request.path === path) {
        throw new DashboardApiError(503, `The ${path} read failed.`)
      }

      return undefined
    },
  }

  return state
}

function queryRow(text: RegExp) {
  return screen.queryAllByRole('row').find((row) => text.test(row.textContent ?? '')) ?? null
}

function getRow(text: RegExp) {
  const found = queryRow(text)

  if (!found) {
    throw new Error(`No row matches ${text}`)
  }

  return found
}

/** The data rows' accessible names are their name cells, so a row is found by everything it shows. */
function findRow(text: RegExp) {
  return waitFor(() => getRow(text))
}

function findPanel(name = '') {
  return screen.findByRole('region', { name: name ? `Sequence counter ${name}` : 'Sequence counter' }).then(within)
}

function cells(text: RegExp) {
  return within(getRow(text))
}

function stat(label: string) {
  return within(screen.getByRole('region', { name: 'Sequences at a glance' }))
    .getByText(label)
    .closest('div')!
}

describe('the counter list', () => {
  it('lists a gap-free yearly counter with its partition, value, last number, and next reset', async () => {
    open(fake)

    const row = await findRow(/INV-2026/)
    const cell = within(row)
    expect(cell.getByText('acme')).toBeTruthy()
    expect(cell.getByText('Gap-free')).toBeTruthy()
    expect(cell.getByText('Year')).toBeTruthy()
    expect(cell.getByText('2026')).toBeTruthy()
    expect(cell.getByText('INV-2026-000003')).toBeTruthy()
    expect(cell.getByText('3')).toBeTruthy()
    expect(row.querySelectorAll('time').length).toBeGreaterThanOrEqual(2)
  })

  it('shows the six served stat cells', async () => {
    open(fake)

    await findRow(/INV-2026/)
    const strip = within(screen.getByRole('region', { name: 'Sequences at a glance' }))
    for (const label of ['Counters', 'Current partitions', 'Gap-free', 'Fast', 'Reported', 'Next reset']) {
      expect(strip.getByText(label)).toBeTruthy()
    }
    await waitFor(() => expect(stat('Counters').textContent).toContain('4'))
    expect(stat('Current partitions').textContent).toContain('3')
  })

  it('names the host scope Host', async () => {
    open(fake)

    expect(within(await findRow(/ticket/)).getByText('Host')).toBeTruthy()
  })

  it('filters by mode through the URL and keeps every chip count', async () => {
    const actor = user()
    const view = open(fake)
    const chips = within(await screen.findByRole('group', { name: 'Filter by mode' }))
    await waitFor(() => expect(chips.getByRole('button', { name: /Fast/ }).textContent).toContain('2'))

    await actor.click(chips.getByRole('button', { name: /Fast/ }))

    await waitFor(() => expect(queryRow(/invoice/)).toBeNull())
    expect(view.location().search).toContain('mode=Fast')
    expect(counterRequests(view).at(-1)?.query?.mode).toBe('Fast')
    expect(getRow(/ticket/)).toBeTruthy()
    expect(chips.getByRole('button', { name: /Gap-free/ }).textContent).toContain('2')
    expect(chips.getByRole('button', { name: /All/ }).textContent).toContain('4')
  })

  it('lists the mode chips in the stat strip order', async () => {
    open(fake)
    const chips = within(await screen.findByRole('group', { name: 'Filter by mode' }))

    expect(chips.getAllByRole('button').map((chip) => chip.textContent?.replace(/[\d,]+$/, ''))).toEqual([
      'All',
      'Gap-free',
      'Fast',
      'Reported',
    ])
  })

  it('starts filtered when the URL holds a mode', async () => {
    const view = open(fake, { path: '?mode=Fast' })

    await findRow(/ticket/)
    expect(queryRow(/invoice/)).toBeNull()
    expect(counterRequests(view)[0]?.query?.mode).toBe('Fast')
  })

  it('lists only host counters under the Host scope', async () => {
    const actor = user()
    const view = open(fake)
    await findRow(/INV-2026/)

    await actor.click(within(screen.getByRole('group', { name: 'Scope' })).getByRole('button', { name: 'Host' }))

    await waitFor(() => expect(queryRow(/invoice/)).toBeNull())
    expect(counterRequests(view).at(-1)?.query?.scope).toBe('host')
    expect(within(await findRow(/ticket/)).getByText('Host')).toBeTruthy()
  })

  it('reads one tenant only once its id is submitted', async () => {
    const actor = user()
    const view = open(fake)
    await findRow(/INV-2026/)
    const before = view.requests.length

    await actor.click(within(screen.getByRole('group', { name: 'Scope' })).getByRole('button', { name: 'Tenant' }))

    expect(view.requests).toHaveLength(before)
    expect(getRow(/ticket/)).toBeTruthy()

    await actor.type(screen.getByRole('searchbox', { name: 'Tenant id' }), 'acme{Enter}')

    await waitFor(() => expect(queryRow(/ticket/)).toBeNull())
    expect(counterRequests(view).at(-1)?.query).toMatchObject({ scope: 'tenant', tenant: 'acme' })
    expect(view.location().search).toContain('tenant=acme')
  })

  it('sends no request for a tenant scope with no tenant id', async () => {
    const view = open(fake, { path: '?scope=tenant' })

    expect(await screen.findByText('Enter a tenant id')).toBeTruthy()
    expect(screen.getByRole('searchbox', { name: 'Tenant id' })).toBeTruthy()
    expect(view.requests.filter((request) => request.path.startsWith('sequences/'))).toEqual([])
  })

  it('drops the tenant from the URL when the scope leaves Tenant', async () => {
    const actor = user()
    const view = open(fake, { path: '?scope=tenant&tenant=acme' })
    await findRow(/INV-2026/)

    await actor.click(within(screen.getByRole('group', { name: 'Scope' })).getByRole('button', { name: 'All' }))

    await findRow(/ticket/)
    expect(view.location().search).not.toContain('tenant')
  })

  it('searches with the text as typed', async () => {
    const actor = user()
    const view = open(fake)
    await findRow(/INV-2026/)

    await actor.type(screen.getByRole('searchbox', { name: 'Search counters' }), 'INV')
    await vi.advanceTimersByTimeAsync(400)

    await waitFor(() => expect(counterRequests(view).at(-1)?.query?.q).toBe('INV'))
  })

  it('lists only current partitions when asked', async () => {
    const actor = user()
    const view = open(fake)
    await findRow(/INV-2025/)

    await actor.click(
      within(screen.getByRole('group', { name: 'Partitions' })).getByRole('button', { name: 'Current only' }),
    )

    await waitFor(() => expect(queryRow(/INV-2025/)).toBeNull())
    expect(counterRequests(view).at(-1)?.query?.current).toBe('true')
  })

  it('loads the next page with the cursor the last page returned', async () => {
    const actor = user()
    const view = open({ counters: rows, pageSize: 2 })
    await findRow(/INV-2025/)

    await actor.click(getRow(/INV-2025/))

    await findRow(/order/)
    expect(counterRequests(view).map((request) => request.query?.after)).toEqual([undefined, '2'])
  })

  it('reads the list again when the window regains focus', async () => {
    const view = open(fake)
    await findRow(/INV-2026/)
    const before = counterRequests(view).length

    window.dispatchEvent(new Event('focus'))

    await waitFor(() => expect(counterRequests(view).length).toBeGreaterThan(before))
  })

  it('sends no request on Refresh or focus while the tenant scope names no tenant', async () => {
    const actor = user()
    const view = open(fake, { path: '?scope=tenant' })
    expect(await screen.findByText('Enter a tenant id')).toBeTruthy()

    await actor.click(screen.getByRole('button', { name: 'Refresh' }))
    window.dispatchEvent(new Event('focus'))
    await vi.advanceTimersByTimeAsync(50)

    expect(moduleRequests(view)).toEqual([])
  })

  it('reads nothing again on Refresh or focus for an open panel while the tenant scope names no tenant', async () => {
    const actor = user()
    const view = open(fake, { path: `?scope=tenant&open=${encodeURIComponent(acmeInvoice.id)}` })
    const panel = await findPanel('invoice')
    await waitFor(() => expect(panel.getAllByRole('row').length).toBeGreaterThan(1))
    const before = moduleRequests(view).length

    await actor.click(screen.getByRole('button', { name: 'Refresh' }))
    window.dispatchEvent(new Event('focus'))
    await vi.advanceTimersByTimeAsync(50)

    expect(moduleRequests(view)).toHaveLength(before)
  })

  it('links each mode card to its mode in the same scope, tenant, and search', async () => {
    open(fake, {
      path: `?scope=tenant&tenant=acme&q=inv&current=true&open=${encodeURIComponent(acmeInvoice.id)}`,
    })
    await findRow(/INV-2026/)

    for (const [label, mode] of [
      ['Gap-free', 'GapFree'],
      ['Fast', 'Fast'],
      ['Reported', 'Reported'],
    ] as const) {
      const query = statLink(label)
      expect(Object.fromEntries(query)).toEqual({ scope: 'tenant', tenant: 'acme', q: 'inv', mode })
    }
  })

  it('marks a counter whose name has no registered policy', async () => {
    open({ counters: [counter({ name: 'adhoc', policySource: 'default' })] })

    expect((await findRow(/adhoc/)).textContent).toContain('Default policy')
  })

  it('says a counter has no formatted number instead of leaving the cell blank', async () => {
    open({ counters: [hostTicket, counter({ name: 'shipment', mode: 'Reported' })] })

    expect(within(await findRow(/ticket/)).getByText('No format')).toBeTruthy()
    expect(cells(/shipment/).getByText('Not issued here')).toBeTruthy()
  })

  it('shows a capped count as 10,000+', async () => {
    open({ counters: rows, summary: summaryOf(rows, { total: 10_000, capped: true }) })

    await waitFor(() => expect(stat('Counters').textContent).toContain('10,000+'))
  })
})

describe('empty and failed reads', () => {
  it('names AddHeadlessSequences when no counter is stored', async () => {
    open({ counters: [] })

    expect(await screen.findByText('No counters yet')).toBeTruthy()
    expect(screen.getByText(/AddHeadlessSequences/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull()
  })

  it('offers Clear filters when a filter matches nothing', async () => {
    const actor = user()
    const view = open({ counters: [hostTicket] }, { path: '?mode=GapFree' })

    expect(await screen.findByText('No counters match')).toBeTruthy()
    await actor.click(screen.getByRole('button', { name: 'Clear filters' }))

    await findRow(/ticket/)
    expect(view.location().search).toBe('')
  })

  it('shows the store failure with Retry and no zero counts', async () => {
    open({ failure: { status: 503, message: 'The sequence store is unavailable.' } })

    const alerts = await screen.findAllByRole('alert')
    expect(alerts.some((alert) => alert.textContent?.includes('The sequence store is unavailable.'))).toBe(true)
    expect(screen.getAllByRole('button', { name: 'Retry' }).length).toBeGreaterThan(0)
    expect(queryRow(/invoice/)).toBeNull()
    expect(stat('Counters').textContent).toContain('–')
    expect(stat('Counters').textContent).not.toContain('0')
  })

  it('keeps the last rows and counts under a failed refresh', async () => {
    const actor = user()
    const state: FakeServer = { counters: rows }
    open(state)
    await findRow(/INV-2026/)
    await waitFor(() => expect(stat('Counters').textContent).toContain('4'))

    state.failure = { status: 503, message: 'The sequence store is unavailable.' }
    await actor.click(screen.getByRole('button', { name: 'Refresh' }))

    expect((await screen.findByText(/What is shown is from the last successful read/)).textContent).toContain(
      'The sequence store is unavailable.',
    )
    expect(getRow(/INV-2026/)).toBeTruthy()
    expect(stat('Counters').textContent).toContain('4')
  })
})

describe('the counter panel', () => {
  it("reads the open panel's partitions again on Refresh", async () => {
    const actor = user()
    const view = open(fake, { path: `?open=${encodeURIComponent(acmeInvoice.id)}` })
    const panel = await findPanel('invoice')
    await waitFor(() =>
      expect(within(panel.getByRole('table', { name: 'Partitions' })).getAllByRole('row')).toHaveLength(3),
    )
    const before = requestsTo(view, 'sequences/counter/partitions').length

    await actor.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(requestsTo(view, 'sequences/counter/partitions').length).toBeGreaterThan(before))
  })

  it('reads a counter opened from the URL again on Refresh and on focus', async () => {
    const actor = user()
    const view = open(fake, { path: `?mode=Fast&open=${encodeURIComponent(acmeInvoice.id)}` })
    const panel = await findPanel('invoice')
    expect(await panel.findByText('INV-{yyyy}-{seq:D6}')).toBeTruthy()
    expect(requestsTo(view, 'sequences/counter')).toHaveLength(1)

    await actor.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(requestsTo(view, 'sequences/counter')).toHaveLength(2))

    window.dispatchEvent(new Event('focus'))
    await waitFor(() => expect(requestsTo(view, 'sequences/counter')).toHaveLength(3))
  })

  it('keeps a counter opened from the URL under the error when its refresh fails', async () => {
    const actor = user()
    const state = failing('sequences/counter')
    open(state, { path: `?mode=Fast&open=${encodeURIComponent(acmeInvoice.id)}` })
    const panel = await findPanel('invoice')
    expect(await panel.findByText('INV-{yyyy}-{seq:D6}')).toBeTruthy()

    state.isFailing = true
    await actor.click(screen.getByRole('button', { name: 'Refresh' }))

    const alert = await panel.findByRole('alert')
    expect(alert.textContent).toContain('The sequences/counter read failed.')
    expect(alert.textContent).toContain('What is shown is from the last successful read.')
    expect(panel.getByText('INV-{yyyy}-{seq:D6}')).toBeTruthy()
  })

  it('shows a policy with a Windows zone id without failing the panel', async () => {
    const actor = user()
    const windows = counter({
      name: 'receipt',
      nextResetAt: '2026-12-31T22:00:00Z',
      policy: { ...invoicePolicy, timeZone: 'Egypt Standard Time' },
    })
    open({ counters: [windows] }, { path: `?open=${encodeURIComponent(windows.id)}` })
    const panel = await findPanel('receipt')
    expect(panel.getByText('Egypt Standard Time')).toBeTruthy()
    const nextReset = panel
      .getAllByRole('button')
      .find((button) => button.querySelector('time')?.getAttribute('datetime') === windows.nextResetAt)

    await actor.click(nextReset!)

    expect((await screen.findByRole('tooltip')).textContent).toContain('2026')
  })

  it('loads more partitions with the cursor the last page returned', async () => {
    const actor = user()
    const older = counter({ name: 'invoice', partition: '2024', isCurrentPartition: false, policy: invoicePolicy })
    const view = open(
      { counters: [acmeInvoice, acmeInvoiceLastYear, older], partitionPageSize: 2 },
      { path: `?open=${encodeURIComponent(acmeInvoice.id)}` },
    )
    const panel = await findPanel('invoice')
    const table = within(panel.getByRole('table', { name: 'Partitions' }))
    await waitFor(() => expect(table.getAllByRole('row')).toHaveLength(3))

    await actor.click(panel.getByRole('button', { name: 'Load more partitions' }))

    await waitFor(() => expect(table.getAllByRole('row')).toHaveLength(4))
    expect(requestsTo(view, 'sequences/counter/partitions').map((request) => request.query?.before)).toEqual([
      undefined,
      '2025',
    ])
    expect(panel.queryByRole('button', { name: 'Load more partitions' })).toBeNull()
  })

  it('shows a failed partitions read as an error, not as no partitions', async () => {
    const state = failing('sequences/counter/partitions')
    state.isFailing = true
    open(state, { path: `?open=${encodeURIComponent(acmeInvoice.id)}` })
    const panel = await findPanel('invoice')

    expect((await panel.findByRole('alert')).textContent).toContain('The sequences/counter/partitions read failed.')
    expect(panel.queryByRole('table', { name: 'Partitions' })).toBeNull()
    expect(panel.getByRole('button', { name: 'Retry' })).toBeTruthy()
  })

  it('opens a clicked row with its policy and partitions, without reading the counter by id', async () => {
    const actor = user()
    const view = open(fake)

    await actor.click(await findRow(/INV-2026/))

    const panel = await findPanel('invoice')
    expect(view.location().search).toContain(`open=${encodeURIComponent(acmeInvoice.id)}`)
    expect(panel.getByText('INV-{yyyy}-{seq:D6}')).toBeTruthy()
    expect(panel.getByText('Africa/Cairo')).toBeTruthy()
    expect(panel.getByText('Fiscal-year start month')).toBeTruthy()
    expect(panel.getAllByText('Gap-free').length).toBeGreaterThan(0)
    const partitions = within(panel.getByRole('table', { name: 'Partitions' }))
    // The table's first row is its header.
    await waitFor(() => expect(partitions.getAllByRole('row')).toHaveLength(3))
    const [, current, closed] = partitions.getAllByRole('row')
    expect(current!.textContent).toContain('2026')
    expect(current!.textContent).toContain('Current')
    expect(closed!.textContent).toContain('2025')
    expect(closed!.textContent).toContain('Closed')
    expect(view.requests.some((request) => request.path === 'sequences/counter')).toBe(false)
  })

  it('opens the counter a URL names', async () => {
    open(fake, { path: `?open=${encodeURIComponent(acmeInvoice.id)}` })

    const panel = await findPanel('invoice')
    expect(await panel.findByText('INV-{yyyy}-{seq:D6}')).toBeTruthy()
  })

  it('reads a counter by id when the list does not hold it', async () => {
    const view = open(fake, { path: `?mode=Fast&open=${encodeURIComponent(acmeInvoice.id)}` })

    const panel = await findPanel('invoice')
    expect(await panel.findByText('INV-{yyyy}-{seq:D6}')).toBeTruthy()
    expect(view.requests.filter((request) => request.path === 'sequences/counter')).toHaveLength(1)
  })

  it('says so when the counter a URL names is not found', async () => {
    open(fake, { path: '?open=acme%2Fgone%2F' })

    expect(await screen.findByText(/No counter with this id is among the newest 1,000 partitions/)).toBeTruthy()
    expect((await findPanel()).getByText('acme/gone/')).toBeTruthy()
  })

  it('clears the URL when the panel closes', async () => {
    const actor = user()
    const view = open(fake, { path: `?open=${encodeURIComponent(acmeInvoice.id)}` })
    const panel = await findPanel('invoice')

    await actor.click(panel.getByRole('button', { name: 'Close details' }))

    await waitFor(() => expect(view.location().search).not.toContain('open='))
  })

  it('offers no control that changes a counter and says reading never issues a number', async () => {
    open(fake, { path: `?open=${encodeURIComponent(acmeInvoice.id)}` })

    const panel = await findPanel('invoice')
    expect(panel.getByText(/reading never issues a number/)).toBeTruthy()
    const changing = /reset|advance|edit|delete|issue|next number|set value/i
    for (const button of screen.getAllByRole('button')) {
      expect(button.textContent ?? '').not.toMatch(changing)
    }
  })

  it('notes that a block-reserving counter stores the end of its last block', async () => {
    const blocks = counter({
      name: 'order',
      policy: { ...acmeInvoice.policy, mode: 'Fast', blockSize: 50, format: null },
    })
    open({ counters: [blocks] }, { path: `?open=${encodeURIComponent(blocks.id)}` })

    const panel = await findPanel('order')
    expect(panel.getByText(/end of the last reserved block/)).toBeTruthy()
  })

  it('says in the panel whether the policy is registered or the default one', async () => {
    const adhoc = counter({ name: 'adhoc', policySource: 'default' })
    open({ counters: [adhoc] }, { path: `?open=${encodeURIComponent(adhoc.id)}` })
    expect((await findPanel('adhoc')).getByText('Default policy')).toBeTruthy()

    cleanup()
    open(fake, { path: `?open=${encodeURIComponent(acmeInvoice.id)}` })
    expect((await findPanel('invoice')).getByText('Registered for this name')).toBeTruthy()
  })
})
