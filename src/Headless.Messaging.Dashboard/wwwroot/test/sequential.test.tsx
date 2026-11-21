import { screen, waitFor, within } from '@testing-library/react'
import type { ApiRequest } from '@headless/dashboard-sdk/data'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { consumer, now, open, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

const sequential = { ...consumer, lane: 'Bus', sequential: true, activeHost: 'lock' }

/** Answers the consumer view's read of the sequential state with `state`, or fails it when `state` is an error. */
function answering(state: unknown) {
  return (request: ApiRequest) => {
    if (request.path !== 'messaging/consumers/sequential') {
      return undefined
    }

    if (state instanceof Error) {
      throw state
    }

    return state
  }
}

function field(scope: HTMLElement, label: string) {
  return within(scope).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent
}

async function openPanel(options: Parameters<typeof open>[0]) {
  const actor = user()
  const view = open(options, { path: 'consumers' })

  await actor.click(await screen.findByRole('row', { name: /Billing\.OrderPlacedConsumer/ }))

  return { view, panel: screen.getByRole('region', { name: /^Consumer / }) }
}

describe('messaging sequential consumer state', () => {
  it('names the host that holds the subscription and the messages waiting there', async () => {
    const { view, panel } = await openPanel({
      consumers: [sequential],
      extra: answering({
        intake: 'lock',
        holders: [{ subscription: consumer.identity, state: 'held', node: 'node-a', waiting: 3 }],
      }),
    })

    await waitFor(() => expect(field(panel, 'Active host')).toBe('node-a'))
    expect(field(panel, 'Waiting on active host')).toBe('3')
    expect(view.requests.find((request) => request.path === 'messaging/consumers/sequential')?.query).toMatchObject({
      identity: consumer.identity,
      lane: 'bus',
    })
  })

  it('says none when no host holds the subscription', async () => {
    const { panel } = await openPanel({
      consumers: [sequential],
      extra: answering({ intake: 'lock', holders: [{ subscription: consumer.identity, state: 'none' }] }),
    })

    await waitFor(() => expect(field(panel, 'Active host')).toBe('None'))
    expect(within(panel).queryByText('Waiting on active host')).toBeNull()
  })

  it('says the holder is unavailable, never none, when the lock store did not answer', async () => {
    const { panel } = await openPanel({
      consumers: [sequential],
      extra: answering({
        intake: 'lock',
        holders: [
          { subscription: consumer.identity, state: 'unavailable', reason: 'The lock store did not answer in time.' },
        ],
      }),
    })

    await waitFor(() => expect(field(panel, 'Active host')).toBe('Unavailable: The lock store did not answer in time.'))
  })

  it('says the holder is unavailable, never none, when the read fails', async () => {
    const { panel } = await openPanel({ consumers: [sequential], extra: answering(new Error('node-b did not answer')) })

    await waitFor(() => expect(field(panel, 'Active host')).toBe('Unavailable: node-b did not answer'))
  })

  it('shows how hosts share it and this node’s waiting count when no lock elects a host', async () => {
    const { panel } = await openPanel({
      consumers: [{ ...sequential, activeHost: 'broker' }],
      extra: answering({ intake: 'broker', holders: [], waiting: 2 }),
    })

    await waitFor(() => expect(field(panel, 'Active host')).toBe('Chosen by the broker'))
    expect(field(panel, 'Waiting here')).toBe('2')
  })

  it('names each Queue-lane subscription’s holder and counts each holding host once', async () => {
    const { panel } = await openPanel({
      consumers: [{ ...sequential, lane: 'Queue' }],
      extra: answering({
        intake: 'lock',
        holders: [
          { subscription: 'OrderPlaced', state: 'held', node: 'node-a', waiting: 4 },
          { subscription: 'OrderShipped', state: 'held', node: 'node-a', waiting: 4 },
          { subscription: 'OrderCancelled', state: 'none' },
        ],
      }),
    })

    await waitFor(() => expect(field(panel, 'Active host (OrderPlaced)')).toBe('node-a'))
    expect(field(panel, 'Active host (OrderShipped)')).toBe('node-a')
    expect(field(panel, 'Active host (OrderCancelled)')).toBe('None')
    expect(within(panel).getAllByText('Waiting on node-a', { selector: 'dt' })).toHaveLength(1)
    expect(field(panel, 'Waiting on node-a')).toBe('4')
  })

  it('shows neither field, and asks nothing, for a consumer that is not sequential', async () => {
    const { view, panel } = await openPanel({
      consumers: [consumer],
      extra: answering({ intake: 'lock', holders: [] }),
    })

    expect(within(panel).getByText('Concurrency')).toBeTruthy()
    expect(within(panel).queryByText('Active host')).toBeNull()
    expect(within(panel).queryByText(/^Waiting/)).toBeNull()
    expect(view.requests.some((request) => request.path === 'messaging/consumers/sequential')).toBe(false)
  })
})
