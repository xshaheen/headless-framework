import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { check, node, now, open, result } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('cluster accessibility', () => {
  const fake = {
    nodes: [
      node({ id: 'orders-api-0@14', isLocal: true }),
      node({ id: 'orders-worker-1@5', state: 'Suspected' as const }),
    ],
    checks: [check('jobs-scheduler', { latest: result('Degraded') }), check('sandbox-dependency')],
  }

  it.each(['', 'health', '?open=orders-worker-1%405', 'health?open=jobs-scheduler'])(
    'the %s view breaks no WCAG A or AA rule axe checks',
    async (path) => {
      open(fake, { path })

      await screen.findByRole('heading', { level: 1 })
      await screen.findByRole('grid')
      expect(await axeViolations()).toEqual([])
    },
  )
})
