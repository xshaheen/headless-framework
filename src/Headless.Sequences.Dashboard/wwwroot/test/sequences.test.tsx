import { screen, waitFor, within } from '@testing-library/react'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  acmeInvoice,
  acmeInvoiceLastYear,
  counter,
  globexOrder,
  hostTicket,
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
  return view.requests.filter((request) => request.path === 'sequences/counters')
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

    // The previous list stays, and nothing is read until an id is submitted.
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
})
