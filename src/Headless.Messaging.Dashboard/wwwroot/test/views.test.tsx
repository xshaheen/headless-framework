import { screen, waitFor, within } from '@testing-library/react'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
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

    await actor.selectOptions(await screen.findByRole('combobox', { name: 'Node' }), 'node-b')

    await waitFor(() => expect(new URLSearchParams(view.location().search).get('node')).toBe('node-b'))
    await waitFor(() => expect(view.requests.some((request) => request.path === 'messaging/received' && request.query?.node === 'node-b')).toBe(true))
  })

  it('offers no node switch without forwarding', async () => {
    open({ received: [message('received', 'Failed', { name: 'a.failed' })] }, { path: 'received' })

    await screen.findByRole('row', { name: /a\.failed/ })
    expect(screen.queryByRole('combobox', { name: 'Node' })).toBeNull()
  })
})

describe('messaging scheduled deliveries', () => {
  it('names a store that cannot list its scheduled deliveries, and why', async () => {
    open({ scheduled: { store: 'primary', supported: false, reason: 'it keeps no schedule table', items: [], totalCount: 0 } }, { path: 'scheduled' })

    expect(await screen.findByText('The primary store cannot list its scheduled deliveries: it keeps no schedule table')).toBeTruthy()
  })
})

describe('messaging consumers', () => {
  it('resets a circuit at once, and confirms forcing one open', async () => {
    const actor = user()
    const view = open({ circuits: [circuit()] }, { path: 'consumers' })

    const table = await screen.findByRole('grid', { name: 'Circuit breakers' })
    await actor.click(await within(table).findByRole('button', { name: 'Reset circuit' }))

    expect(await screen.findByText('Reset circuit: 1 done')).toBeTruthy()
    expect(view.requests.find((request) => request.path === 'messaging/commands/messaging.reset-circuit')?.body).toMatchObject({
      ids: ['node-a~Billing.OrderPlacedConsumer'],
    })

    await actor.click(within(table).getByRole('button', { name: 'Force open' }))
    expect(await screen.findByRole('dialog', { name: 'Force open' })).toBeTruthy()
  })
})

describe('messaging nodes', () => {
  it('cannot read a peer without forwarding', async () => {
    const actor = user()
    open({ nodes: { ...twoNodes, discovery: null } }, { path: 'nodes' })

    await actor.click(await screen.findByRole('row', { name: /node-b/ }))
    const button = within(screen.getByRole('region', { name: 'Node node-b' })).getByRole('button', { name: 'Read this node' })

    expect(button.getAttribute('aria-disabled')).toBe('true')
    expect(described(button)).toBe('Forwarding is not configured')
  })

  it('reads a peer through ?node= when forwarding is on', async () => {
    const actor = user()
    const view = open({ nodes: twoNodes }, { path: 'nodes' })

    await actor.click(await screen.findByRole('row', { name: /node-b/ }))
    await actor.click(within(screen.getByRole('region', { name: 'Node node-b' })).getByRole('button', { name: 'Read this node' }))

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

    expect(await screen.findByText('No operator has acted on a stored message yet.')).toBeTruthy()
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Target' }), 'Inbox')

    await waitFor(() => expect(new URLSearchParams(view.location().search).get('targetKind')).toBe('Inbox'))
    await waitFor(() => expect(view.requests.some((request) => request.path === 'messaging/receipts' && request.query?.targetKind === 'Inbox')).toBe(true))
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
