import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { circuit, message, now, open, twoNodes, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(() => {
  vi.useRealTimers()
  localStorage.clear()
})

const rows = {
  received: [
    message('received', 'Failed', { name: 'orders.placed' }),
    message('received', 'Succeeded', { name: 'orders.shipped' }),
  ],
  published: [message('published', 'Failed', { name: 'orders.placed' })],
}

describe('messaging accessibility', () => {
  it.each([
    '',
    'published',
    'received',
    'scheduled',
    'consumers',
    'contracts',
    'nodes',
    'unknown-lanes',
    'operator-log',
  ])('the %s view breaks no WCAG A or AA rule axe checks', async (path) => {
    open({ ...rows, nodes: twoNodes, circuits: [circuit()] }, { path })

    await screen.findByRole('heading', { level: 1 })
    expect(await axeViolations()).toEqual([])
  })

  it('the received list with a message open in the panel breaks no rule', async () => {
    const actor = user()
    open(rows, { path: 'received' })

    await actor.click(await screen.findByRole('row', { name: /orders\.placed/ }))
    await screen.findByRole('region', { name: /^Message / })

    expect(await axeViolations()).toEqual([])
  })
})
