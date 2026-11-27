import { describe, expect, it } from 'vitest'
import leases from '../src/module.ts'

describe('leases module definition', () => {
  it('serves the platform product under the leases id', () => {
    expect(leases.id).toBe('leases')
    expect(leases.product).toBe('platform')
  })

  it('adds one Leases entry with the lock icon and the leases count', () => {
    const entries = leases.sidebar.flatMap((section) => section.entries)

    expect(leases.sidebar.map((section) => section.label)).toEqual(['Operations'])
    expect(entries).toEqual([{ id: 'leases.nav.leases', label: 'Leases', route: '', icon: 'lock', countKey: 'leases' }])
  })

  it('loads each view as its own lazy chunk', () => {
    expect(leases.routes.map((route) => route.path)).toEqual(['', 'locks'])
    for (const route of leases.routes) {
      expect(typeof route.lazy).toBe('function')
    }
  })

  it('opens a lease in the fenced view and a lock in the locks view', () => {
    expect(leases.objectKinds['fenced-lease'].route('acme/ledger-export/batch%2F7')).toBe(
      '?open=acme%2Fledger-export%2Fbatch%252F7',
    )
    expect(leases.objectKinds.lock.route('orders:rebuild')).toBe('locks?open=orders%3Arebuild')
  })
})
