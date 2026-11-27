import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { lease, lock, now, open } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('leases accessibility', () => {
  const live = lease({ resource: 'export/1', progressContract: 'export-cursor/v1', progressSizeBytes: 1229 })
  const fake = {
    leases: [live, lease({ resource: 'stuck/1', status: 'Expired' as const, state: 'Expired' as const })],
    locks: [lock({ resource: 'orders:rebuild', ttlMilliseconds: null, leaseId: null })],
  }

  it.each(['', `?open=${encodeURIComponent(live.id)}`, 'locks', 'locks?open=orders%3Arebuild'])(
    'the %s view breaks no WCAG A or AA rule axe checks',
    async (path) => {
      open(fake, { path })

      await screen.findByRole('heading', { level: 1 })
      await screen.findByRole('grid')
      expect(await axeViolations()).toEqual([])
    },
  )
})
