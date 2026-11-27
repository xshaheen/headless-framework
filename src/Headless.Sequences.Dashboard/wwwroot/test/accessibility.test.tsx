import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { acmeInvoice, acmeInvoiceLastYear, hostTicket, now, open } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('sequences accessibility', () => {
  const fake = { counters: [acmeInvoice, acmeInvoiceLastYear, hostTicket] }

  it.each(['', `?open=${encodeURIComponent(acmeInvoice.id)}`])(
    'the list at "%s" breaks no WCAG A or AA rule axe checks',
    async (path) => {
      open(fake, { path })

      await screen.findByRole('grid')
      await screen.findAllByRole('row', { name: /invoice/ })
      expect(await axeViolations()).toEqual([])
    },
  )

  it('the empty list breaks no rule', async () => {
    open({ counters: [] })

    await screen.findByText('No counters yet')
    expect(await axeViolations()).toEqual([])
  })

  it('the failed list breaks no rule', async () => {
    open({ failure: { status: 503, message: 'The sequence store is unavailable.' } })

    await screen.findAllByRole('alert')
    expect(await axeViolations()).toEqual([])
  })
})
