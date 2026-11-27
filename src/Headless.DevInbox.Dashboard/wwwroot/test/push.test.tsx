import { screen, within } from '@testing-library/react'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { now, open, push } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('push view', () => {
  it('lists target, title and body, data keys, and sender, with no provider column', async () => {
    open({ push: [push({ senderName: 'mobile' })] }, { path: 'push' })

    const row = await screen.findByRole('row', { name: /device-token-1/ })
    expect(within(row).getByText('Order shipped · Your order is on its way.')).toBeTruthy()
    expect(within(row).getByText('orderId')).toBeTruthy()
    expect(within(row).getByText('mobile')).toBeTruthy()
    expect(screen.queryByRole('columnheader', { name: 'Provider' })).toBeNull()
  })

  it('marks an oversized push in the list', async () => {
    open({ push: [push({ exceedsPayloadLimit: true })] }, { path: 'push' })

    expect(within(await screen.findByRole('row', { name: /device-token-1/ })).getByText('Over limit')).toBeTruthy()
  })

  it('shows the data payload on the Overview beside the facts, with no tab of its own', async () => {
    const normal = push()
    open({ push: [normal] }, { path: `push?open=${normal.id}` })

    await screen.findByText('120 bytes')
    expect(screen.getByRole('heading', { name: 'Data' })).toBeTruthy()
    expect(screen.getByText(/"orderId"/)).toBeTruthy()
    expect(screen.queryByRole('tab', { name: 'Data' })).toBeNull()
    expect(within(screen.getByRole('region', { name: 'Sent as' })).getByText('Device token')).toBeTruthy()
  })

  it('warns about an oversized push and its size', async () => {
    const large = push({ payloadSizeBytes: 5_120, exceedsPayloadLimit: true })
    open({ push: [large] }, { path: `push?open=${large.id}` })

    expect(await screen.findByText(/Over the 4,096-byte limit/)).toBeTruthy()
    expect(screen.getByText('5,120 bytes')).toBeTruthy()
  })

  it('shows no warning for a normal push', async () => {
    const normal = push()
    open({ push: [normal] }, { path: `push?open=${normal.id}` })

    await screen.findByText('120 bytes')
    expect(screen.queryByText(/Over the 4,096-byte limit/)).toBeNull()
  })

  it('says a data-only push is data-only and shows no title field', async () => {
    const data = push({ title: null, body: null, isDataOnly: true })
    open({ push: [data] }, { path: `push?open=${data.id}` })

    expect(await screen.findByText('Data-only push: wakes the app and shows nothing.')).toBeTruthy()
    expect(screen.queryByText('Title')).toBeNull()
  })
})
