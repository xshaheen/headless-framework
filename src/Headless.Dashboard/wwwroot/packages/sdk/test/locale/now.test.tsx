import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider, relativeTickOf, useNow } from '../../src/locale/index.ts'

function Clock({ period }: { period: number | null }) {
  const now = useNow(period)

  return <span>{now}</span>
}

describe('useNow', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-10T12:00:00Z'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('moves on every period while shown', () => {
    render(<Clock period={1_000} />)
    const start = Date.parse('2026-11-10T12:00:00Z')

    expect(screen.getByText(String(start))).toBeTruthy()

    act(() => {
      vi.advanceTimersByTime(3_000)
    })

    expect(screen.getByText(String(start + 3_000))).toBeTruthy()
  })

  it('stays put when stopped', () => {
    render(<Clock period={null} />)
    const start = Date.parse('2026-11-10T12:00:00Z')

    act(() => {
      vi.advanceTimersByTime(5_000)
    })

    expect(screen.getByText(String(start))).toBeTruthy()
  })

  it('keeps a relative time moving without new data', () => {
    const at = Date.parse('2026-11-10T12:00:03Z')

    function Next() {
      const now = useNow(relativeTickOf(at))

      return <span>{at <= now ? 'due' : 'ahead'}</span>
    }

    render(
      <LocaleProvider>
        <Next />
      </LocaleProvider>,
    )

    expect(screen.getByText('ahead')).toBeTruthy()

    act(() => {
      vi.advanceTimersByTime(4_000)
    })

    expect(screen.getByText('due')).toBeTruthy()
  })
})

describe('relativeTickOf', () => {
  it('reads every second under a minute away, then every 30 s, then every minute', () => {
    const now = 1_000_000_000

    expect(relativeTickOf(now + 59_000, now)).toBe(1_000)
    expect(relativeTickOf(now - 120_000, now)).toBe(30_000)
    expect(relativeTickOf(now + 7_200_000, now)).toBe(60_000)
  })
})
