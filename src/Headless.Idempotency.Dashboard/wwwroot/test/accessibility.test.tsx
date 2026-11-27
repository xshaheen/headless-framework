import { screen } from '@testing-library/react'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { found, key, notFound, now, open, records, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
})

describe('idempotency accessibility', () => {
  it('the page before a lookup breaks no WCAG A or AA rule axe checks', async () => {
    open()

    await screen.findByRole('heading', { level: 1 })
    await screen.findByText('Why this screen is read-only')
    expect(await axeViolations()).toEqual([])
  })

  it.each([
    ['completed', found(records.completed), /Completed/],
    ['in-flight', found(records.inFlight), /In flight/],
    ['past-retention', found(records.pastRetention), /Kept past retention/],
    ['stalled', found(records.stalled), /Stalled/],
    ['released', found(records.released), /Released/],
    ['not-admitted', found(records.notAdmitted), /Not admitted yet/],
    ['not-found', notFound(), /No record for this key/],
    ['error', new DashboardApiError(503, 'The idempotency store is unavailable. Try again.'), /unavailable/],
  ])('the %s result breaks no rule', async (_, answer, text) => {
    const actor = user()
    const { container } = open({ lookups: [answer] })

    await actor.type(await screen.findByRole('textbox', { name: /^Idempotency key/ }), key)
    await actor.click(screen.getByRole('button', { name: 'Look up' }))
    await screen.findAllByText(text)

    // Scoped to the module: React Aria leaves a pending button's empty announcement node in the body, outside the
    // module and outside what this view draws.
    expect(await axeViolations(container)).toEqual([])
  })
})
