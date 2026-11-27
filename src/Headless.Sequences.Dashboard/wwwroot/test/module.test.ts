import { describe, expect, it } from 'vitest'
import sequences from '../src/module.ts'

describe('sequences module definition', () => {
  it('serves the platform product under the sequences id', () => {
    expect(sequences.id).toBe('sequences')
    expect(sequences.product).toBe('platform')
  })

  it('shows one Sequences entry with the hash icon and the stored-counter count', () => {
    const entries = sequences.sidebar.flatMap((section) => section.entries)

    expect(entries).toEqual([
      { id: 'sequences.nav.sequences', label: 'Sequences', route: '', icon: 'hash', countKey: 'counters' },
    ])
  })

  it('loads the view as its own lazy chunk', () => {
    expect(sequences.routes.map((route) => route.path)).toEqual([''])
    expect(typeof sequences.routes[0]?.lazy).toBe('function')
  })

  it('opens a counter in the list by its escaped id', () => {
    expect(sequences.objectKinds['sequence-counter'].route('acme/invoice/2026')).toBe('?open=acme%2Finvoice%2F2026')
  })
})
