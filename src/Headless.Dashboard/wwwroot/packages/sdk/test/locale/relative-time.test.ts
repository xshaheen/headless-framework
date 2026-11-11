import { describe, expect, it } from 'vitest'
import {
  createRelativeClock,
  formatRelativeTime,
  msUntilRelativeChange,
  relativeTimeParts,
} from '../../src/locale/relative-time.ts'

const s = 1_000
const min = 60 * s
const h = 60 * min
const d = 24 * h
const now = Date.parse('2026-11-11T12:00:00Z')

describe('relativeTimeParts', () => {
  it('uses the largest unit that fits and counts whole units toward zero', () => {
    expect(relativeTimeParts(now - 30 * s, now)).toEqual({ value: -30, unit: 'second' })
    expect(relativeTimeParts(now - (5 * min + 59 * s), now)).toEqual({ value: -5, unit: 'minute' })
    expect(relativeTimeParts(now - 23 * h, now)).toEqual({ value: -23, unit: 'hour' })
    expect(relativeTimeParts(now - 29 * d, now)).toEqual({ value: -29, unit: 'day' })
    expect(relativeTimeParts(now - 45 * d, now)).toEqual({ value: -1, unit: 'month' })
    expect(relativeTimeParts(now - 400 * d, now)).toEqual({ value: -1, unit: 'year' })
    expect(relativeTimeParts(now + 3 * h + 10, now)).toEqual({ value: 3, unit: 'hour' })
  })

  it('reads a moment under a second away as zero, never minus zero', () => {
    expect(Object.is(relativeTimeParts(now - 400, now).value, 0)).toBe(true)
  })
})

describe('formatRelativeTime', () => {
  it('writes the age in the locale’s words', () => {
    expect(formatRelativeTime(now - 5 * min, now, 'en', 'long')).toBe('5 minutes ago')
    expect(formatRelativeTime(now + 2 * h + s, now, 'en', 'long')).toBe('in 2 hours')
    expect(formatRelativeTime(now - 5 * min, now, 'en')).toBe('5 min. ago')
    expect(formatRelativeTime(now - 5 * min, now, 'ar')).toContain('دقائق')
    expect(formatRelativeTime(now - 3 * h, now, 'en', 'narrow')).toBe('3h ago')
  })

  it('stays numeric: a day ago is 1 day ago, never yesterday, and a day ahead never tomorrow', () => {
    expect(formatRelativeTime(now - 26 * h, now, 'en', 'long')).toBe('1 day ago')
    expect(formatRelativeTime(now + 26 * h, now, 'en', 'long')).toBe('in 1 day')
  })

  it('floors toward zero where rounding would add a unit', () => {
    expect(formatRelativeTime(now - 90 * s, now, 'en')).toBe('1 min. ago')
    expect(formatRelativeTime(now - (23 * h + 59 * min), now, 'en')).toBe('23 hr. ago')
  })

  it('says now instead of zero seconds, and hands back text that is not a time', () => {
    expect(formatRelativeTime(now - 200, now, 'en')).toBe('now')
    expect(formatRelativeTime('not a time', now, 'en')).toBe('not a time')
  })
})

describe('msUntilRelativeChange', () => {
  // Stepping by the returned wait must land exactly where the parts change, never before.
  function changesExactlyAfter(at: number) {
    const wait = msUntilRelativeChange(at, now)
    const before = relativeTimeParts(at, now + wait - 1)
    const after = relativeTimeParts(at, now + wait)

    return (
      wait > 0 &&
      JSON.stringify(before) === JSON.stringify(relativeTimeParts(at, now)) &&
      JSON.stringify(after) !== JSON.stringify(before)
    )
  }

  it('is the gap to the next whole unit for a past moment', () => {
    expect(msUntilRelativeChange(now - (30 * s + 250), now)).toBe(750)
    expect(msUntilRelativeChange(now - (5 * min + 20 * s), now)).toBe(40 * s)
    expect(msUntilRelativeChange(now - (3 * h + 15 * min), now)).toBe(45 * min)
  })

  it('stops at the step to a larger unit', () => {
    expect(msUntilRelativeChange(now - 59 * s, now)).toBe(s)
    // 361 days reads 12 months; 1 year starts at 365 days, before a 13th month would.
    expect(msUntilRelativeChange(now - 361 * d, now)).toBe(4 * d)
  })

  it('lands on the change for moments behind and ahead', () => {
    for (const offset of [0, 1, 999, 59 * s, 61 * s, 59 * min + 59 * s, 23 * h, 29 * d + h, 364 * d]) {
      expect(changesExactlyAfter(now - offset)).toBe(true)

      if (offset > 0) {
        expect(changesExactlyAfter(now + offset)).toBe(true)
      }
    }
  })
})

describe('createRelativeClock', () => {
  // A hand-wound clock: `advance` moves time and fires the one pending timer when it is due.
  function harness() {
    let time = now
    let pending: { run: () => void; at: number } | undefined
    const waits: number[] = []
    const clock = createRelativeClock({
      now: () => time,
      setTimer: (run, ms) => {
        waits.push(ms)
        pending = { run, at: time + ms }

        return 0 as unknown as ReturnType<typeof setTimeout>
      },
      clearTimer: () => {
        pending = undefined
      },
    })

    return {
      clock,
      waits,
      hasTimer: () => pending !== undefined,
      advance(ms: number) {
        time += ms
        const due = pending

        if (due && due.at <= time) {
          pending = undefined
          due.run()
        }
      },
    }
  }

  it('wakes when the watched moment next reads differently, and keeps going', () => {
    const { clock, waits, advance } = harness()
    const at = now - (4 * min + 50 * s)
    const texts: string[] = []
    clock.watch(at, () => texts.push(formatRelativeTime(at, clock.now(), 'en', 'long')))

    expect(waits).toEqual([10 * s])
    advance(9 * s)
    expect(texts).toEqual([])
    advance(s)
    expect(texts).toEqual(['5 minutes ago'])
    advance(min)
    expect(texts).toEqual(['5 minutes ago', '6 minutes ago'])
  })

  it('runs one timer, sleeping for the soonest of several moments', () => {
    const { clock, waits } = harness()
    clock.watch(now - 3 * h, () => undefined)
    clock.watch(now - 20 * s, () => undefined)

    expect(waits).toEqual([h, s])
  })

  it('holds one time for every reader between wake-ups', () => {
    const { clock, advance } = harness()
    clock.watch(now - 2 * h, () => undefined)
    advance(5 * min)

    expect(clock.now()).toBe(now)
  })

  it('stops the timer when the last watcher leaves, then reads the real time', () => {
    const { clock, hasTimer, advance } = harness()
    const stop = clock.watch(now - 2 * h, () => undefined)

    expect(hasTimer()).toBe(true)
    stop()
    expect(hasTimer()).toBe(false)
    advance(5 * min)
    expect(clock.now()).toBe(now + 5 * min)
  })
})
