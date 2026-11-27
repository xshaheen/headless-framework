import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { now, open } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('cluster accessibility', () => {
  it.each(['', 'health'])('the %s view breaks no WCAG A or AA rule axe checks', async (path) => {
    open({}, { path })

    await screen.findByRole('heading', { level: 1 })
    expect(await axeViolations()).toEqual([])
  })
})
