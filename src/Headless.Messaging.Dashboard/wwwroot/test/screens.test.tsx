import { screen, waitFor, within } from '@testing-library/react'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { envelopeBody } from '../src/envelope.ts'
import { statusParts, sumHours } from '../src/insight.tsx'
import { consumer, described, message, now, open, twoNodes, user, type FakeServer } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

function cellsOf(table: HTMLElement) {
  return within(table)
    .getAllByRole('row')
    .map((row) => [...row.querySelectorAll('th, td')].map((cell) => cell.textContent))
}

function searches(requests: readonly ApiRequest[]) {
  return requests.filter((request) => request.method === 'POST' && request.path.endsWith('/search'))
}

describe('messaging list search', () => {
  it('sends a content search once, in the body, on Enter, and never in the URL', async () => {
    const actor = user()
    const matched = message('received', 'Failed', { name: 'OrderPlaced' })
    const view = open(
      {
        received: [matched, message('received', 'Succeeded', { name: 'OrderShipped' })],
        extra: (request) =>
          request.method === 'POST' && request.path === 'messaging/received/search'
            ? { items: [matched], count: 1, countCapped: false, nextCursor: null, unavailable: [], incomplete: false }
            : undefined,
      },
      { path: 'received' },
    )

    await screen.findByRole('row', { name: /OrderShipped/ })
    const box = screen.getByRole('searchbox', { name: 'Search received message content' })
    await actor.type(box, 'ORD-991')
    // Typing alone reads nothing: the server pays for every search.
    await vi.advanceTimersByTimeAsync(1_000)
    expect(searches(view.requests)).toHaveLength(0)

    await actor.keyboard('{Enter}')

    await waitFor(() => expect(searches(view.requests)).toHaveLength(1))
    expect(searches(view.requests)[0]!.body).toMatchObject({ content: 'ORD-991', limit: 100 })
    expect(view.location().search).not.toContain('ORD-991')
    await waitFor(() => expect(screen.queryByRole('row', { name: /OrderShipped/ })).toBeNull())
    // The search shows as a chip to clear, which ends it.
    const chips = screen.getByRole('group', { name: 'Set filters' })
    await actor.click(within(chips).getByRole('button', { name: /Clear content/ }))
    await screen.findByRole('row', { name: /OrderShipped/ })
  })

  it('says a search that ran out of time may be missing results, with Retry, never that nothing matched', async () => {
    const actor = user()
    const view = open(
      {
        received: [message('received', 'Succeeded')],
        extra: (request) =>
          request.method === 'POST' && request.path === 'messaging/received/search'
            ? { items: [], count: 0, countCapped: false, nextCursor: null, unavailable: [], incomplete: true }
            : undefined,
      },
      { path: 'received' },
    )

    await screen.findByRole('row', { name: /OrderPlaced/ })
    await actor.type(screen.getByRole('searchbox', { name: 'Search received message content' }), 'gone{Enter}')

    expect(await screen.findByText('Search did not finish; results may be missing.')).toBeTruthy()
    expect(screen.getAllByRole('status').some((region) => region.textContent === 'Search did not finish.')).toBe(true)
    expect(screen.queryByText(/^No received messages/)).toBeNull()
    expect(screen.getByText('The search ran out of time before it found a match; results may be missing.')).toBeTruthy()

    await actor.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(searches(view.requests)).toHaveLength(2))
  })

  it('confirms every match of a search as an Operation, with the text in the body and never the URL', async () => {
    const actor = user()
    const rows = [message('received', 'Failed'), message('received', 'Failed')]
    const view = open(
      {
        received: rows,
        extra: (request) => {
          if (request.method === 'POST' && request.path === 'messaging/received/search') {
            return {
              items: rows,
              count: 240,
              countCapped: false,
              nextCursor: 'next',
              unavailable: [],
              incomplete: false,
            }
          }

          if (request.path === 'messaging/commands/messaging.re-execute/confirm') {
            return {
              operationId: 'op1',
              count: 240,
              confirmation: 'typed',
              sample: [],
              token: 't',
              expiresAt: '',
              chunkSize: 500,
            }
          }

          return undefined
        },
      },
      { path: 'received?status=Failed&terminal=true' },
    )

    await screen.findAllByRole('row', { name: /OrderPlaced/ })
    await actor.type(screen.getByRole('searchbox', { name: 'Search received message content' }), 'ORD-991{Enter}')
    await waitFor(() => expect(screen.getByRole('group', { name: 'Set filters' })).toBeTruthy())
    await actor.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await actor.click(await screen.findByRole('button', { name: /^Select all 240/ }))

    // The bar names the search without its text.
    expect(screen.getByText(/^All 240 matching .* that match the search$/)).toBeTruthy()
    await actor.click(screen.getByRole('button', { name: /^Re-execute 240/ }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'card service back')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
        filter: { status: 'Failed', terminalOnly: true, content: 'ORD-991' },
      }),
    )
    expect(view.requests.every((request) => !JSON.stringify(request.query ?? {}).includes('ORD-991'))).toBe(true)
    expect(view.location().search).not.toContain('ORD-991')
  })

  it('finds a message by correlation id through the search body', async () => {
    const actor = user()
    const view = open(
      {
        published: [message('published', 'Succeeded')],
        extra: (request) =>
          request.method === 'POST' && request.path === 'messaging/published/search'
            ? { items: [], count: 0, countCapped: false, nextCursor: null, unavailable: [], incomplete: false }
            : undefined,
      },
      { path: 'published' },
    )

    await screen.findByRole('row', { name: /OrderPlaced/ })
    await actor.click(screen.getByRole('button', { name: /More filters/ }))
    await actor.type(await screen.findByRole('searchbox', { name: 'Correlation id' }), 'c-42{Enter}')

    await waitFor(() => expect(searches(view.requests)[0]?.body).toMatchObject({ correlationId: 'c-42' }))
    expect(searches(view.requests)[0]?.body).not.toHaveProperty('content')
    expect(view.location().search).not.toContain('c-42')
  })
})

describe('messaging list filters and columns', () => {
  it('tells two messages of one name apart by their id, and shows the exact time', async () => {
    open(
      {
        received: [
          message('received', 'Succeeded', { messageId: 'ord-1' }),
          message('received', 'Succeeded', { messageId: 'ord-2' }),
        ],
      },
      { path: 'received' },
    )

    const rows = await screen.findAllByRole('row', { name: /OrderPlaced/ })
    expect(rows.map((row) => within(row).queryByText(/^ord-\d$/)?.textContent)).toEqual(['ord-1', 'ord-2'])
    expect(screen.getByRole('columnheader', { name: /Message id/ })).toBeTruthy()
    expect(screen.getAllByText(/Nov 5, 2026, 09:50:00/).length).toBeGreaterThanOrEqual(2)
  })

  it('sorts by name on the server when its header is pressed', async () => {
    const actor = user()
    const view = open({ received: [message('received', 'Succeeded')] }, { path: 'received' })

    await screen.findByRole('row', { name: /OrderPlaced/ })
    const header = screen.getByRole('columnheader', { name: 'Message' })
    await actor.click(within(header).getByRole('button', { name: 'Message' }))

    await waitFor(() =>
      expect(
        view.requests.some(
          (request) =>
            request.path === 'messaging/received' &&
            request.query?.sort === 'name' &&
            request.query.direction === 'ascending',
        ),
      ).toBe(true),
    )
  })

  it('reads the lane, message id, and range a link carries, and runs every match over the same scope', async () => {
    const actor = user()
    const failed = [message('published', 'Failed', { store: 'orders', lane: 'Queue', added: '2026-11-01T12:00:00Z' })]
    const view = open(
      {
        published: failed,
        nodes: { items: [{ name: 'node-a', isLocal: true, stores: ['primary', 'orders'] }], discovery: null },
        extra: (request) => {
          if (request.path === 'messaging/published') {
            return { items: failed, count: 240, countCapped: false, nextCursor: 'next', unavailable: [] }
          }

          if (request.path === 'messaging/commands/messaging.re-send/confirm') {
            return {
              operationId: 'op1',
              count: 240,
              confirmation: 'typed',
              sample: [],
              token: 't',
              expiresAt: '',
              chunkSize: 500,
            }
          }

          return undefined
        },
      },
      { path: 'published?status=Failed&terminal=true&store=orders&lane=queue&from=2026-11-01&to=2026-11-02' },
    )

    await screen.findAllByRole('row', { name: /OrderPlaced/ })
    const read = view.requests.find((request) => request.path === 'messaging/published')!
    expect(read.query).toMatchObject({ lane: 'Queue' })
    expect(read.query?.from).toBeTruthy()
    expect(read.query?.before).toBeTruthy()

    await actor.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await actor.click(await screen.findByRole('button', { name: /^Select all 240/ }))
    await actor.click(screen.getByRole('button', { name: /^Re-send 240/ }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'broker back')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
        filter: { store: 'orders', lane: 'Queue', from: read.query?.from, before: read.query?.before },
      }),
    )
  })
})

describe('messaging detail panel', () => {
  it('groups the fields, reads the correlation from the headers, and shows the body without the headers', async () => {
    const actor = user()
    const row = message('received', 'Succeeded', { name: 'a.done' })
    open(
      {
        received: [row],
        extra: (request) =>
          request.path === `messaging/messages/received/${row.id}`
            ? {
                row,
                headers: { 'headless-msg-id': 'm-1', 'headless-corr-id': 'c-42' },
                content:
                  '{"Headers":{"headless-msg-id":"m-1","headless-corr-id":"c-42"},"Value":{"orderId":12345678901234567890}}',
              }
            : undefined,
      },
      { path: 'received' },
    )

    await actor.click(await screen.findByRole('row', { name: /a\.done/ }))
    const panel = screen.getByRole('region', { name: `Message ${row.id}` })
    for (const group of ['Identity', 'Delivery', 'Correlation']) {
      expect(await within(panel).findByRole('region', { name: group })).toBeTruthy()
    }
    expect(within(within(panel).getByRole('region', { name: 'Correlation' })).getByText('c-42')).toBeTruthy()

    await actor.click(within(panel).getByRole('tab', { name: 'Body' }))
    const body = within(panel).getByRole('region', { name: 'Body' })
    expect(body.textContent).toContain('12345678901234567890')
    expect(body.textContent).not.toContain('headless-corr-id')
  })

  it('shows each inbox generation with its number, origin, expiry, and recovery', async () => {
    const actor = user()
    const original = message('received', 'Failed', { messageId: 'm-gen' })
    const replay = message('received', 'Succeeded', { messageId: 'm-gen' })
    open(
      {
        received: [original, replay],
        extra: (request) =>
          request.path === 'messaging/received/generations'
            ? {
                items: [
                  {
                    ...original,
                    generation: {
                      number: 0,
                      incarnationId: 'i0',
                      origin: 'original',
                      isCurrent: false,
                      expiresAt: '2026-11-12T10:00:00Z',
                      recovery: 'routable',
                    },
                  },
                  {
                    ...replay,
                    generation: {
                      number: 1,
                      incarnationId: 'i1',
                      origin: 'replay',
                      isCurrent: true,
                      replayOfIncarnationId: 'i0',
                      replayOfId: original.id,
                      recovery: 'orphaned',
                    },
                  },
                ],
              }
            : undefined,
      },
      { path: 'received' },
    )

    await actor.click((await screen.findAllByRole('row', { name: /OrderPlaced/ }))[1]!)
    const panel = screen.getByRole('region', { name: /^Message / })
    await actor.click(within(panel).getByRole('tab', { name: 'Generations' }))

    expect(
      await within(panel).findByText(/Generation 1, current, replay, orphaned: no host registers its consumer/),
    ).toBeTruthy()
    expect(within(panel).getByText(/Generation 0, retired, original, expires Nov 12, 2026, 10:00:00/)).toBeTruthy()
  })
})

describe('messaging dashboard insight', () => {
  const realtime = {
    seconds: [1_700_000_000, 1_700_000_001, 1_700_000_002],
    publishedPerSecond: [3, null, 4],
    consumedPerSecond: [2, null, 5],
    consumeElapsedMs: [12, null, 9],
  }

  it('draws the rate and the latency on one time axis, an unmeasured second a gap in both', async () => {
    const actor = user()
    open({ realtime }, { path: '' })

    const card = await screen.findByRole('region', { name: 'Live throughput' })
    const rate = await within(card).findByRole('img', { name: /^Message rate:/ })
    const latency = within(card).getByRole('img', { name: /^Consume latency:/ })
    expect(rate.getAttribute('aria-label')).toContain('unmeasured buckets are gaps')
    expect(latency.getAttribute('aria-label')).toContain('unmeasured buckets are gaps')
    // One axis: both name the same first and last second.
    const range = (label: string | null) => /from (.*?) to (.*?) /.exec(label ?? '')?.slice(1)
    expect(range(rate.getAttribute('aria-label'))).toEqual(range(latency.getAttribute('aria-label')))

    await actor.click(within(card).getAllByRole('button', { name: /^Show list:/ })[0]!)
    const table = within(card).getByRole('table', { name: 'Message rate' })
    expect(cellsOf(table)[2]!.slice(1)).toEqual(['not measured', 'not measured'])
  })

  it('shows capped counts with no shares and a list instead of the donut', async () => {
    const counts = {
      'received.all': 14_500,
      'received.succeeded': 4_210,
      'received.failed': 10_000,
      'received.retrying': 0,
    }
    open(
      {
        extra: (request) =>
          request.path === 'counts'
            ? {
                counts: Object.fromEntries(
                  Object.entries(counts).map(([key, count]) => [
                    key,
                    { count, capped: key === 'received.failed' || key === 'received.all' },
                  ]),
                ),
                stale: [],
              }
            : undefined,
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Messages by status' })
    const table = await within(card).findByRole('table', { name: 'Received by status' })
    const rows = cellsOf(table)
    expect(rows).toContainEqual(['Failed', '10,000+'])
    expect(rows).toContainEqual(['Succeeded', '4,210'])
    expect(table.textContent).not.toContain('%')
    expect(within(card).queryByRole('img', { name: /^Received by status/ })).toBeNull()
  })

  it('names a store whose hours were not read, and never counts it as zero', async () => {
    const actor = user()
    open(
      {
        hourly: {
          interval: 'PT1H',
          zone: 'UTC',
          asOf: now.toISOString(),
          hourStarts: ['2026-11-05T08:00:00Z', '2026-11-05T09:00:00Z'],
          stores: [
            { store: 'primary', received: { succeeded: [5, null], failed: [1, 0] }, published: null },
            { store: 'billing' },
          ],
          unavailable: ['billing'],
        },
      },
      { path: '' },
    )

    const card = await screen.findByRole('region', { name: 'Last 24 hours' })
    expect(await within(card).findByText('Not counted, as these stores did not answer: billing.')).toBeTruthy()
    await actor.click(within(card).getByRole('button', { name: /^Show list:/ }))
    const rows = cellsOf(within(card).getByRole('table', { name: 'Received per hour' }))
    expect(rows[1]!.slice(1)).toEqual(['5', '1'])
    expect(rows[2]![1]).toBe('not measured')
  })
})

describe('messaging insight sums', () => {
  it('reads an hour as unmeasured when any store did not count it', () => {
    expect(
      sumHours(
        [
          { succeeded: [1, 2], failed: [0, 0] },
          { succeeded: [3, null], failed: [0, 0] },
        ],
        'succeeded',
        2,
      ),
    ).toEqual([4, null])
  })

  it('derives the waiting received rows only from exact counts', () => {
    const exact = (value: number) => ({ value, capped: false })
    const counts = {
      'received.all': exact(10),
      'received.succeeded': exact(6),
      'received.failed': exact(1),
      'received.retrying': exact(1),
    }

    expect(statusParts('received', counts)).toMatchObject({
      complete: true,
      parts: [{ name: 'Succeeded' }, { name: 'Failed' }, { name: 'Retrying' }, { name: 'Waiting', count: exact(2) }],
    })
    expect(statusParts('received', { ...counts, 'received.all': { value: 10, capped: true } }).complete).toBe(false)
  })
})

describe('messaging envelope body', () => {
  it('cuts the stored value from the envelope, keeping large integers exact', () => {
    expect(envelopeBody('{"Headers":{"a":"b"},"Value":{"id":12345678901234567890}}')).toBe(
      '{"id":12345678901234567890}',
    )
    expect(envelopeBody('{ "headers": {}, "value": "text, with } brace" }')).toBe('"text, with } brace"')
  })

  it('leaves content that is not an envelope alone', () => {
    expect(envelopeBody('{"orderId":7}')).toBeUndefined()
    expect(envelopeBody('not json')).toBeUndefined()
  })
})

describe('messaging consumers implementation', () => {
  it('shows the type that implements each message and the method that handles it', async () => {
    const actor = user()
    open(
      {
        consumers: [
          {
            ...consumer,
            messages: [
              {
                name: 'OrderPlaced',
                contractVersion: '1',
                consumerType: 'Billing.OrderPlacedConsumer',
                handlerMethod: 'ConsumeAsync',
              },
            ],
          },
        ],
      },
      { path: 'consumers' },
    )

    const row = await screen.findByRole('row', { name: /Billing\.OrderPlacedConsumer/ })
    expect(within(row).getByText('OrderPlacedConsumer')).toBeTruthy()
    await actor.click(row)
    const panel = screen.getByRole('region', { name: /^Consumer / })
    expect(within(panel).getByText('Billing.OrderPlacedConsumer.ConsumeAsync')).toBeTruthy()
  })
})

describe('messaging scheduled name filter', () => {
  it('asks the server for one message name', async () => {
    const view = open({}, { path: 'scheduled?name=OrderPlaced' })

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'messaging/scheduled')?.query).toMatchObject({
        name: 'OrderPlaced',
      }),
    )
  })
})

describe('messaging node ping', () => {
  const nodes = {
    items: [
      { name: 'node-a', isLocal: true, stores: ['primary'] },
      {
        name: 'node-b',
        isLocal: false,
        stores: [],
        state: 'answering',
        instances: [{ address: 'http://10.0.4.7', port: 8080, tags: ['blue', 'eu'] }],
      },
      { name: 'node-c', isLocal: false, stores: [], state: 'answering', instances: [] },
    ],
    discovery: '2 nodes',
  }

  function pinging(fake: FakeServer = {}): FakeServer {
    return {
      nodes,
      ...fake,
      extra: (request) =>
        request.path === 'messaging/nodes/ping'
          ? {
              items: [
                { node: 'node-b', outcome: 'answered', latencyMs: 42 },
                { node: 'node-c', outcome: 'timeout', reason: 'No answer in 3 s' },
              ],
              discovery: '2 nodes',
            }
          : undefined,
    }
  }

  it('shows each node’s address, port, and tags', async () => {
    open({ nodes }, { path: 'nodes' })

    const row = await screen.findByRole('row', { name: /node-b/ })
    expect(within(row).getByText('http://10.0.4.7:8080')).toBeTruthy()
    expect(within(row).getByText('blue, eu')).toBeTruthy()
  })

  it('pings every node and shows a timeout on its own row while the other shows its latency', async () => {
    const actor = user()
    const view = open(pinging(), { path: 'nodes' })

    await screen.findByRole('row', { name: /node-b/ })
    await actor.click(screen.getByRole('button', { name: 'Ping all' }))

    expect(view.requests.find((request) => request.path === 'messaging/nodes/ping')?.body).toEqual({})
    expect(await within(screen.getByRole('row', { name: /node-b/ })).findByText('42 ms')).toBeTruthy()
    expect(within(screen.getByRole('row', { name: /node-c/ })).getByText('Timed out')).toBeTruthy()
  })

  it('says what Ping all found once it settles, since the results land in cells away from the button', async () => {
    const actor = user()
    open(pinging(), { path: 'nodes' })

    await screen.findByRole('row', { name: /node-b/ })
    await actor.click(screen.getByRole('button', { name: 'Ping all' }))

    await waitFor(() =>
      expect(screen.getAllByRole('status').some((region) => region.textContent === 'Ping all: 1 of 2 answered.')).toBe(
        true,
      ),
    )
  })

  it('says one node’s ping from the same region on every press', async () => {
    const actor = user()
    open(pinging(), { path: 'nodes' })
    const said = () => screen.getAllByRole('status').find((region) => region.textContent === 'Ping node-b: 42 ms.')

    await actor.click(await screen.findByRole('button', { name: 'Ping node-b' }))
    await waitFor(() => expect(said()).toBeTruthy())
    const region = said()

    await actor.click(screen.getByRole('button', { name: 'Ping node-b' }))
    await waitFor(() => expect(said()).toBe(region))
  })

  it('pings one node by name from its row', async () => {
    const actor = user()
    const view = open(pinging(), { path: 'nodes' })

    await actor.click(await screen.findByRole('button', { name: 'Ping node-b' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path === 'messaging/nodes/ping')?.body).toEqual({
        node: 'node-b',
      }),
    )
  })

  it('shows the server’s refusal on the node it pinged', async () => {
    const actor = user()
    open(
      {
        nodes,
        extra: (request) => {
          if (request.path === 'messaging/nodes/ping') {
            throw new DashboardApiError(404, "Discovery lists no Messaging node named 'node-b'.")
          }

          return undefined
        },
      },
      { path: 'nodes' },
    )

    await actor.click(await screen.findByRole('button', { name: 'Ping node-b' }))

    const row = screen.getByRole('row', { name: /node-b/ })
    expect(await within(row).findByText('Refused')).toBeTruthy()
    expect(within(row).getByTitle(/Discovery lists no Messaging node named 'node-b'/)).toBeTruthy()
  })

  it('disables Ping for a viewer, with Needs operate permission', async () => {
    open({ nodes }, { path: 'nodes', permissions: { canOperate: false } })

    const all = await screen.findByRole('button', { name: 'Ping all' })
    expect(all.getAttribute('aria-disabled')).toBe('true')
    expect(described(all)).toBe('Needs operate permission')
    // The table draws its rows after the toolbar, so a row's Ping can land a render after Ping all.
    expect(described(await screen.findByRole('button', { name: 'Ping node-b' }))).toContain('Needs operate permission')
  })

  it('offers no ping without forwarding', async () => {
    open({ nodes: { ...twoNodes, discovery: null } }, { path: 'nodes' })

    const all = await screen.findByRole('button', { name: 'Ping all' })
    expect(described(all)).toBe('Forwarding is off, so there is no other node to ping')
  })
})
