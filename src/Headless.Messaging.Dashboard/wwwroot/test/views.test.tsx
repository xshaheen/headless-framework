import { act, screen, waitFor, within } from '@testing-library/react'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import messaging from '../src/module.ts'
import { circuit, described, message, now, open, twoNodes, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

describe('messaging dashboard', () => {
  it('reads – for every figure the server has not measured', async () => {
    open({}, { path: '' })

    const strip = await screen.findByRole('region', { name: 'Messaging at a glance' })
    await waitFor(() => expect(within(strip).getAllByText('–').length).toBeGreaterThanOrEqual(2))
  })

  it('shows the sidebar counts in the strip from the shell’s shared counts', async () => {
    const view = open({ counts: { 'received.failed': 12, 'published.failed': 3 } }, { path: '' })

    const strip = await screen.findByRole('region', { name: 'Messaging at a glance' })
    await waitFor(() => expect(within(strip).getByText('12')).toBeTruthy())
    expect(view.client.getQueryData(['shell', 'counts', 'messaging'])).toBeTruthy()
  })

  it('switches node through ?node=, and later reads carry it', async () => {
    const actor = user()
    const view = open({ nodes: twoNodes }, { path: 'received' })

    await actor.click(await screen.findByRole('button', { name: /Node/ }))
    await actor.click(await screen.findByRole('option', { name: 'node-b' }))

    await waitFor(() => expect(new URLSearchParams(view.location().search).get('node')).toBe('node-b'))
    await waitFor(() =>
      expect(
        view.requests.some((request) => request.path === 'messaging/received' && request.query?.node === 'node-b'),
      ).toBe(true),
    )
  })

  it('offers no node switch without forwarding', async () => {
    open({ received: [message('received', 'Failed', { name: 'a.failed' })] }, { path: 'received' })

    await screen.findByRole('row', { name: /a\.failed/ })
    expect(screen.queryByRole('button', { name: /Node/ })).toBeNull()
  })
})

/** The row of a card that carries `text`, so a figure is read beside the name it belongs to. */
function cardRow(card: HTMLElement, text: string) {
  const row = within(card)
    .getAllByRole('listitem')
    .find((item) => item.textContent?.includes(text))

  if (!row) {
    throw new Error(`No row of the card reads "${text}"`)
  }

  return row
}

describe('messaging dashboard health cards', () => {
  it('shows each store’s backlog with its oldest wait, and names a store that did not answer', async () => {
    open(
      {
        backlog: {
          asOf: now.toISOString(),
          stores: [
            {
              store: 'primary',
              publishedPending: 128,
              oldestPublishedSeconds: 42,
              receivedPending: 0,
            },
          ],
          unavailable: ['billing'],
        },
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Backlog' })
    await within(card).findByText('Waiting to be sent')
    const sending = cardRow(card, 'Waiting to be sent')
    expect(within(sending).getByText('128')).toBeTruthy()
    expect(within(sending).getByText(/oldest for 42 sec/)).toBeTruthy()
    // Nothing waits to be handled, so that row has a count and no oldest wait.
    const handling = cardRow(card, 'Waiting to be handled')
    expect(within(handling).getByText('0')).toBeTruthy()
    expect(within(handling).queryByText(/oldest/)).toBeNull()
    // A store that did not answer may hold more, so the card gives no total.
    expect(within(card).getByRole('status').textContent).toContain('Not answering: billing')
    expect(within(card).queryByText(/128 waiting/)).toBeNull()
  })

  it('totals the backlog when every store answered, and leaves an outbox without received rows', async () => {
    open(
      {
        backlog: {
          asOf: now.toISOString(),
          stores: [
            { store: 'primary', publishedPending: 3, oldestPublishedSeconds: 5, receivedPending: 2 },
            { store: 'billing', publishedPending: 4, oldestPublishedSeconds: 90 },
          ],
          unavailable: [],
        },
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Backlog' })
    expect(await within(card).findByText('9 waiting')).toBeTruthy()
    expect(within(card).getAllByText('Waiting to be sent')).toHaveLength(2)
    expect(within(card).getAllByText('Waiting to be handled')).toHaveLength(1)
  })

  it('lists only the circuits that are not closed, each opening its consumer', async () => {
    open(
      {
        circuits: [
          circuit({ state: 'Closed' }),
          circuit({
            id: 'node-a~Orders.ShipConsumer',
            consumer: 'Orders.ShipConsumer',
            state: 'Open',
            consecutiveFailures: 5,
            failureThreshold: 5,
            estimatedRemainingOpenDuration: '00:00:18',
          }),
        ],
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Circuits' })
    const link = await within(card).findByRole('link', { name: 'Orders.ShipConsumer' })
    expect(link.getAttribute('href')).toContain('consumers?open=node-a~Orders.ShipConsumer')
    expect(within(card).getByText('1 open')).toBeTruthy()
    expect(within(card).getByText('Open · 5 of 5 failures')).toBeTruthy()
    expect(within(card).getByText('retries in 18 s')).toBeTruthy()
    expect(within(card).queryByText('Billing.OrderPlacedConsumer')).toBeNull()
  })

  it('says no circuit is open when every one is closed, and counts them', async () => {
    open({ circuits: [circuit({ state: 'Closed' })] }, { path: '' })

    const card = await screen.findByRole('region', { name: 'Circuits' })
    expect(await within(card).findByText('No circuit is open.')).toBeTruthy()
    expect(within(card).getByText('1 closed')).toBeTruthy()
  })

  it('names the broker of each lane and the storage of each outbox', async () => {
    open(
      {
        providers: {
          transports: { bus: 'nats', queue: 'rabbitmq' },
          outboxes: [{ name: 'primary', storage: 'PostgreSql' }],
        },
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Providers' })
    await within(card).findByText('Bus lane')
    expect(within(cardRow(card, 'Bus lane')).getByText('nats')).toBeTruthy()
    expect(within(cardRow(card, 'Queue lane')).getByText('rabbitmq')).toBeTruthy()
    const outbox = cardRow(card, 'Outbox')
    expect(within(outbox).getByText('primary')).toBeTruthy()
    expect(within(outbox).getByText('PostgreSql')).toBeTruthy()
  })

  it('shows Retry on a card whose read failed, never an empty card', async () => {
    open(
      {
        extra: (request) => {
          if (request.path === 'messaging/backlog') {
            throw new DashboardApiError(503, 'The store is down')
          }

          return undefined
        },
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Backlog' })
    expect((await within(card).findByRole('alert')).textContent).toContain('The store is down')
    expect(within(card).getByRole('button', { name: 'Retry' })).toBeTruthy()
  })
})

describe('messaging node persistence', () => {
  const nodeOf = (view: ReturnType<typeof open>) => new URLSearchParams(view.location().search).get('node')

  it('keeps ?node= across a sidebar route and a module command, and the next read carries it', async () => {
    const view = open({ nodes: twoNodes }, { path: 'received?node=node-b' })
    await screen.findByRole('button', { name: /Node/ })

    // The sidebar's route for All published, as the shell links it: no `node`.
    void view.navigate('published')
    await waitFor(() => expect(view.location().pathname).toBe('/messaging/published'))
    await waitFor(() => expect(nodeOf(view)).toBe('node-b'))
    await waitFor(() =>
      expect(
        view.requests.some((request) => request.path === 'messaging/published' && request.query?.node === 'node-b'),
      ).toBe(true),
    )
    expect(
      view.requests.some((request) => request.path === 'messaging/published' && request.query?.node !== 'node-b'),
    ).toBe(false)

    const command = messaging.commands!.find((candidate) => candidate.id === 'messaging.go-received-failed')!
    void command.run({ navigate: (to: string) => void view.navigate(to) } as Parameters<typeof command.run>[0])
    await waitFor(() => expect(view.location().search).toContain('status=Failed'))
    await waitFor(() => expect(nodeOf(view)).toBe('node-b'))
  })

  it('drops the node only on an explicit switch to the local node', async () => {
    const actor = user()
    const view = open({ nodes: twoNodes }, { path: 'received?node=node-b' })

    await actor.click(await screen.findByRole('button', { name: /Node/ }))
    await actor.click(await screen.findByRole('option', { name: 'node-a (this node)' }))
    await waitFor(() => expect(nodeOf(view)).toBeNull())

    void view.navigate('published')
    await waitFor(() => expect(view.location().pathname).toBe('/messaging/published'))
    await screen.findByRole('button', { name: /Node/ })
    expect(nodeOf(view)).toBeNull()
  })
})

describe('messaging attention gaps', () => {
  const answer = (section: Record<string, unknown>) => (request: { path: string }) =>
    request.path === 'needs-you'
      ? { modules: [{ id: 'messaging', displayName: 'Messaging', items: [], ...section }] }
      : undefined
  const lane = {
    id: 'unknown-lane:primary:received',
    title: 'Rows on an unknown lane',
    severity: 'warning',
    count: 3,
    capped: false,
  }

  it('leads a failure group with its consumer and follows it with the count as a fact of its own', async () => {
    const group = {
      id: 'failed:received:v1:9f2c41:Billing.Orders.OrderPlacedConsumer:primary',
      title: '37 messages received by Billing.Orders.OrderPlacedConsumer failed for good',
      severity: 'critical',
      count: 37,
      capped: false,
    }
    open({ extra: answer({ stale: false, items: [group] }) }, { path: '' })

    const card = await screen.findByRole('region', { name: 'Needs attention' })
    // The two parts are separate texts, so a narrow card wraps each whole rather than cutting the sentence mid-way.
    const consumer = await within(card).findByText('Billing.Orders.OrderPlacedConsumer')
    expect(within(card).getByText('37 messages failed for good')).toBeTruthy()
    expect(consumer.closest('a')?.getAttribute('aria-label')).toBe(group.title)
    expect(within(card).getByTitle(group.title)).toBeTruthy()
  })

  it('shows Retry, never the healthy empty message, when Messaging did not answer', async () => {
    open({ extra: answer({ stale: true }) }, { path: '' })

    expect(await screen.findByText('Messaging did not answer, so what needs attention is unknown.')).toBeTruthy()
    expect(screen.getAllByRole('button', { name: 'Retry' }).length).toBeGreaterThan(0)
    expect(screen.queryByText('Nothing in Messaging needs attention.')).toBeNull()
  })

  it('shows the items and names the stores that did not answer', async () => {
    open({ extra: answer({ stale: false, unavailable: ['archive'], items: [lane] }) }, { path: 'unknown-lanes' })

    await screen.findByRole('row', { name: /Rows on an unknown lane/ })
    expect(screen.getByText('Not answering: archive. Items from these may be missing.')).toBeTruthy()
  })

  it('never says every row is on a lane when a store did not answer', async () => {
    open({ extra: answer({ stale: false, unavailable: ['archive'] }) }, { path: 'unknown-lanes' })

    expect(await screen.findByText('Not answering: archive. Items from these may be missing.')).toBeTruthy()
    expect(screen.queryByText('Every stored row is on a lane some consumer registers.')).toBeNull()
  })

  it('says every row is on a lane only when every source answered', async () => {
    open({ extra: answer({ stale: false }) }, { path: 'unknown-lanes' })

    expect(await screen.findByText('Every stored row is on a lane some consumer registers.')).toBeTruthy()
  })
})

describe('messaging scheduled deliveries', () => {
  it('names a store that cannot list its scheduled deliveries, and why', async () => {
    open(
      {
        scheduled: {
          store: 'primary',
          supported: false,
          reason: 'it keeps no schedule table',
          items: [],
          totalCount: 0,
        },
      },
      { path: 'scheduled' },
    )

    expect(
      await screen.findByText('The primary store cannot list its scheduled deliveries: it keeps no schedule table'),
    ).toBeTruthy()
  })
})

describe('messaging consumers', () => {
  it('resets a circuit at once, and confirms forcing one open', async () => {
    const actor = user()
    const view = open({ circuits: [circuit()] }, { path: 'consumers' })

    const table = await screen.findByRole('grid', { name: 'Circuit breakers' })
    await actor.click(await within(table).findByRole('button', { name: 'Reset circuit' }))

    expect(await screen.findByText('Reset circuit: 1 done')).toBeTruthy()
    expect(
      view.requests.find((request) => request.path === 'messaging/commands/messaging.reset-circuit')?.body,
    ).toMatchObject({
      ids: ['node-a~Billing.OrderPlacedConsumer'],
    })

    await actor.click(within(table).getByRole('button', { name: 'Force open' }))
    expect(await screen.findByRole('alertdialog', { name: 'Force open' })).toBeTruthy()
  })
})

describe('messaging stale views', () => {
  it('shows Stale at once on a view opened after the module went stale', async () => {
    const view = open({ circuits: [circuit()] }, { path: 'contracts' })
    await screen.findByRole('heading', { name: 'Contracts' })

    view.stream.push('module.stale', { module: 'messaging', stale: true })
    await act(() => view.navigate('consumers'))

    expect(await screen.findByText('Stale. Live updates stopped, so these consumers may be out of date.')).toBeTruthy()
  })

  it('shows Stale at once on a view of a node that went stale before it opened, and not on the local node', async () => {
    const view = open({ nodes: twoNodes }, { path: 'nodes' })
    await screen.findByRole('row', { name: /node-b/ })

    view.stream.push('node.stale', { module: 'messaging', node: 'node-b', stale: true })
    await act(() => view.navigate('scheduled?node=node-b'))

    expect(await screen.findByText('Stale. Live updates stopped, so these deliveries may be out of date.')).toBeTruthy()

    await act(() => view.navigate('scheduled?node=node-a'))
    await waitFor(() => expect(view.location().search).toBe('?node=node-a'))
    expect(screen.queryByText('Stale. Live updates stopped, so these deliveries may be out of date.')).toBeNull()
  })
})

describe('messaging nodes', () => {
  it('reads the nodes again on a stream reset, since no change carries them', async () => {
    const view = open({ nodes: twoNodes }, { path: 'nodes' })
    await screen.findByRole('row', { name: /node-b/ })
    const reads = () => view.requests.filter((request) => request.path === 'messaging/nodes').length
    const before = reads()

    act(() => view.stream.push('reset', {}))

    await waitFor(() => expect(reads()).toBe(before + 1))
  })

  it('cannot read a peer without forwarding', async () => {
    const actor = user()
    open({ nodes: { ...twoNodes, discovery: null } }, { path: 'nodes' })

    await actor.click(await screen.findByRole('row', { name: /node-b/ }))
    const button = within(screen.getByRole('region', { name: 'Node node-b' })).getByRole('button', {
      name: 'Read this node',
    })

    expect(button.getAttribute('aria-disabled')).toBe('true')
    expect(described(button)).toBe('Forwarding is not configured')
  })

  it('reads a peer through ?node= when forwarding is on', async () => {
    const actor = user()
    const view = open({ nodes: twoNodes }, { path: 'nodes' })

    await actor.click(await screen.findByRole('row', { name: /node-b/ }))
    await actor.click(
      within(screen.getByRole('region', { name: 'Node node-b' })).getByRole('button', { name: 'Read this node' }),
    )

    await waitFor(() => expect(new URLSearchParams(view.location().search).get('node')).toBe('node-b'))
  })
})

describe('messaging operator log', () => {
  it('says a store keeps no ledger when the server answers 501', async () => {
    open(
      {
        extra: (request) => {
          if (request.path === 'messaging/receipts') {
            throw new DashboardApiError(501, 'Not implemented')
          }

          return undefined
        },
      },
      { path: 'operator-log' },
    )

    expect(await screen.findByText('This store keeps no operator ledger, so no intervention is recorded.')).toBeTruthy()
  })

  it('filters by target through ?targetKind=', async () => {
    const actor = user()
    const view = open({}, { path: 'operator-log' })

    expect(await screen.findByText(/No operator has acted on a stored message yet\./)).toBeTruthy()
    await actor.click(screen.getByRole('button', { name: /Target/ }))
    await actor.click(await screen.findByRole('option', { name: 'Received' }))

    await waitFor(() => expect(new URLSearchParams(view.location().search).get('targetKind')).toBe('Inbox'))
    await waitFor(() =>
      expect(
        view.requests.some((request) => request.path === 'messaging/receipts' && request.query?.targetKind === 'Inbox'),
      ).toBe(true),
    )
  })

  it('names a receipt by the command the operator chose, and by its message id when it has no name', async () => {
    open(
      {
        extra: (request) =>
          request.path === 'messaging/receipts'
            ? {
                items: [
                  {
                    operationId: '0192f3c1-0000-7000-8000-000000000001',
                    targetKind: 'Inbox',
                    operationType: 'ForceReprocess',
                    outcome: 'Applied',
                    actor: 'ops',
                    reason: 'stock service restored',
                    createdAt: '2026-11-08T05:00:00Z',
                    messageId: 'order-8452',
                  },
                ],
              }
            : undefined,
      },
      { path: 'operator-log' },
    )

    expect(await screen.findByText('Re-execute · Received')).toBeTruthy()
    expect(screen.getByText('order-8452')).toBeTruthy()
  })
})
