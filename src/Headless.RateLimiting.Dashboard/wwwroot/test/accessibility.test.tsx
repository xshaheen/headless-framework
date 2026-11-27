import { screen } from '@testing-library/react'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { now, open, subject, user, window } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
})

describe('rate limits accessibility', () => {
  it('the page with no declared purposes breaks no WCAG A or AA rule axe checks', async () => {
    open({ catalogue: { purposes: [] } })

    await screen.findByRole('heading', { level: 1 })
    await screen.findByText(/AttemptLimiterOptions\.Purposes so the screen lists it/)
    expect(await axeViolations()).toEqual([])
  })

  it.each([
    ['locked-out', window(5)],
    ['within-limit', window(2)],
    ['error', new DashboardApiError(503, "The attempt limiter for 'sign-in.password' is unavailable.")],
  ])('the %s result breaks no rule', async (_, answer) => {
    const actor = user()
    const { container } = open({ lookups: [answer] })

    await actor.type(await screen.findByRole('textbox', { name: /^Subject/ }), subject)
    await actor.click(screen.getByRole('button', { name: 'Look up' }))
    await screen.findAllByText(/used|unavailable/)

    // Scoped to the module: React Aria leaves a pending button's empty announcement node in the body, outside the
    // module and outside what this view draws.
    expect(await axeViolations(container)).toEqual([])
  })
})
