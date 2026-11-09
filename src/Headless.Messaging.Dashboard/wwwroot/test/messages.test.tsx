import { screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { change, described, message, now, open, twoNodes, user, type FakeServer } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

function list(fake: FakeServer, path: string, options: Parameters<typeof open>[1] = {}) {
  return open(fake, { path, ...options })
}

describe('messaging lists', () => {
  it('shows each row’s state as mark and label', async () => {
    list(
      {
        received: [
          message('received', 'Failed', { name: 'a.failed' }),
          message('received', 'Failed', { name: 'a.retrying', terminal: false }),
          message('received', 'Succeeded', { name: 'a.done' }),
          message('received', 'Failed', { name: 'a.held', isHeld: true }),
        ],
      },
      'received',
    )

    const expected: [string, string, string][] = [
      ['a.failed', 'Failed', 'hd-tone-fail'],
      ['a.retrying', 'Retrying', 'hd-tone-proc'],
      ['a.done', 'Succeeded', 'hd-tone-ok'],
      ['a.held', 'Held', 'hd-tone-off'],
    ]

    for (const [name, label, tone] of expected) {
      const mark = within(await screen.findByRole('row', { name: new RegExp(name) })).getByText(label)
      expect(mark.className).toContain(tone)
    }
  })

  it('shows the failed received list’s cause chips with the counts the failure groups serve, and a chip filters by its pair', async () => {
    const actor = user()
    const view = list(
      {
        received: [
          message('received', 'Failed'),
          message('received', 'Failed', { consumer: 'orders:billing-handler', failureFingerprint: 'v1:deserialization' }),
        ],
        attention: [
          { id: 'failed:received:v1:9f2c41:Billing.OrderPlacedConsumer:primary', title: '7 failed', severity: 'critical', count: 7, capped: false },
          { id: 'failed:received:v1:deserialization:orders:billing-handler:primary', title: '2 failed', severity: 'critical', count: 2, capped: false },
          { id: 'failed:published:v1:expired::primary', title: '1 failed', severity: 'critical', count: 1, capped: false },
        ],
      },
      'received?status=Failed&terminal=true',
    )

    const chips = await screen.findByRole('group', { name: 'Filter by cause' })
    await waitFor(() => expect(within(chips).getAllByRole('button')).toHaveLength(3))
    expect(within(chips).getByRole('button', { name: /Billing\.OrderPlacedConsumer/ }).textContent).toContain('7')
    expect(within(chips).getByRole('button', { name: /orders:billing-handler/ }).textContent).toContain('2')
    // One consumer can fail several ways, so a chip names its cause too.
    expect(within(chips).getByRole('button', { name: /orders:billing-handler · deserialization/ })).toBeTruthy()

    await actor.click(within(chips).getByRole('button', { name: /orders:billing-handler/ }))

    await waitFor(() => expect(screen.queryByRole('row', { name: /Billing\.OrderPlacedConsumer/ })).toBeNull())
    const params = new URLSearchParams(view.location().search)
    expect([params.get('fingerprint'), params.get('consumer')]).toEqual(['v1:deserialization', 'orders:billing-handler'])
    expect(
      view.requests.some(
        (request) =>
          request.path === 'messaging/received' && request.query?.fingerprint === 'v1:deserialization' && request.query?.consumer === 'orders:billing-handler',
      ),
    ).toBe(true)
  })

  it('selects every matching row past the loaded page and confirms it as an Operation on the shown store', async () => {
    const actor = user()
    const failed = [message('published', 'Failed', { store: 'orders' }), message('published', 'Failed', { store: 'orders' })]
    const view = list(
      {
        published: failed,
        nodes: { items: [{ name: 'node-a', isLocal: true, stores: ['primary', 'orders'] }], discovery: null },
        extra: (request) => {
          if (request.path === 'messaging/published') {
            // The other store did not answer: the count reads as at least, and the Operation still runs on this one.
            return { items: failed, count: 240, countCapped: false, nextCursor: 'next', unavailable: ['primary'] }
          }

          if (request.path === 'messaging/commands/messaging.re-send/confirm') {
            return { operationId: 'op1', count: 240, confirmation: 'typed', sample: [], token: 't', expiresAt: '', chunkSize: 500 }
          }

          return undefined
        },
      },
      'published?status=Failed&terminal=true&store=orders',
    )

    await screen.findAllByRole('row', { name: /OrderPlaced/ })
    expect(screen.getByText('Some stores did not answer, so their messages are missing: primary.')).toBeTruthy()
    await actor.click(screen.getByRole('checkbox', { name: 'Select all loaded' }))
    await actor.click(await screen.findByRole('button', { name: /^Select all 240/ }))

    expect(screen.getByText(/^All 240\+? matching failed published messages$/)).toBeTruthy()
    await actor.click(screen.getByRole('button', { name: /^Re-send 240/ }))
    await actor.type(await screen.findByRole('textbox', { name: /Reason/ }), 'broker back')
    await actor.click(screen.getByRole('button', { name: 'Count rows' }))

    await waitFor(() =>
      expect(view.requests.find((request) => request.path.endsWith('/confirm'))?.body).toMatchObject({
        filter: { store: 'orders', status: 'Failed', terminalOnly: true },
        reason: 'broker back',
      }),
    )
  })

  it('keeps a row open when a live change moves it out of the filter, showing its new state', async () => {
    const actor = user()
    const failed = message('received', 'Failed', { name: 'a.failed' })
    const view = list({ received: [failed] }, 'received?status=Failed&terminal=true')

    await actor.click(await screen.findByRole('row', { name: /a\.failed/ }))
    const panel = screen.getByRole('region', { name: `Message ${failed.id}` })

    view.stream.push('change', change('received', { ...failed, status: 'Succeeded', terminal: false, retries: 4, version: '2' }))

    await waitFor(() => expect(within(screen.getByRole('region', { name: `Message ${failed.id}` })).getAllByText('Succeeded').length).toBeGreaterThan(0))
    expect(panel.isConnected || screen.getByRole('region', { name: `Message ${failed.id}` })).toBeTruthy()
    expect(new URLSearchParams(view.location().search).get('open')).toBe(failed.id)
  })

  it('disables Re-execute for a viewer, with Needs operate permission', async () => {
    const failed = message('received', 'Failed', { name: 'a.failed' })
    list({ received: [failed] }, 'received', { permissions: { canOperate: false } })

    await user().click(await screen.findByRole('row', { name: /a\.failed/ }))
    const panel = screen.getByRole('region', { name: `Message ${failed.id}` })

    for (const name of ['Re-execute', 'Hold', 'Purge']) {
      const button = await within(panel).findByRole('button', { name })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(described(button)).toBe('Needs operate permission')
    }
  })

  it('shows the server’s reason on an action it refuses', async () => {
    const done = message('received', 'Succeeded', { name: 'a.done' })
    list({ received: [done] }, 'received')

    await user().click(await screen.findByRole('row', { name: /a\.done/ }))
    const button = await within(screen.getByRole('region', { name: `Message ${done.id}` })).findByRole('button', { name: 'Re-execute' })

    await waitFor(() => expect(described(button)).toBe('Not failed'))
  })

  it('reads another node’s list through node, on that node’s topic', async () => {
    const view = list({ nodes: twoNodes, received: [message('received', 'Failed', { name: 'a.failed' })] }, 'received?node=node-b')

    await screen.findByRole('row', { name: /a\.failed/ })
    expect(view.requests.filter((request) => request.path === 'messaging/received').every((request) => request.query?.node === 'node-b')).toBe(true)
    expect(view.stream.topics()).toContain('messaging.received@node-b')
  })

  it('offers a consume-only message in the Message filter when the node declares no contracts', async () => {
    list({ contracts: [], received: [message('received', 'Succeeded')] }, 'received')

    const select = await screen.findByRole('combobox', { name: 'Message' })
    await waitFor(() => expect(within(select).getByRole('option', { name: 'OrderPlaced' })).toBeTruthy())
  })
})
