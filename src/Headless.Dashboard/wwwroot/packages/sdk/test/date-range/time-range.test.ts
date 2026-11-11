import { describe, expect, it } from 'vitest'
import { calendarDayOf, msUntilNextDay, parseCalendarDay } from '../../src/date-range/calendar-date.ts'
import {
  timeRangeFromSearch,
  timeRangeToSearch,
  toCustomTimeRange,
  toListRange,
  type TimeRange,
} from '../../src/date-range/time-range.ts'

const h = 3_600_000
const week: TimeRange = { kind: 'preset', preset: '7d' }
const firstTen: TimeRange = { kind: 'custom', start: '2026-11-01', end: '2026-11-10' }

describe('a time range in the URL', () => {
  it('reads a preset and a custom range back as they went in', () => {
    for (const range of [week, firstTen]) {
      expect(timeRangeFromSearch(timeRangeToSearch(range))).toEqual(range)
    }
  })

  it('clears the other kind’s params, so switching leaves no stale key', () => {
    expect(timeRangeToSearch(week)).toEqual({ range: '7d', from: undefined, to: undefined })
    expect(timeRangeToSearch(firstTen)).toEqual({ range: undefined, from: '2026-11-01', to: '2026-11-10' })
  })

  it('writes no params for the default or for no range, and reads a bare URL as the default', () => {
    expect(timeRangeToSearch(undefined)).toEqual({ range: undefined, from: undefined, to: undefined })
    expect(timeRangeToSearch(week, week).range).toBeUndefined()
    expect(timeRangeFromSearch({})).toBeUndefined()
    expect(timeRangeFromSearch({}, { defaultRange: week })).toEqual(week)
  })

  it('reads a hand-edited URL as the default instead of throwing', () => {
    const bad: Record<string, unknown>[] = [
      { range: '90d' },
      { range: 7 },
      { from: '2026-11-01' },
      { from: '2026-02-30', to: '2026-03-01' },
      { from: 'yesterday', to: 'today' },
      { from: '2026-11-10', to: '2026-11-01' },
    ]

    for (const search of bad) {
      expect(timeRangeFromSearch(search, { defaultRange: week })).toEqual(week)
    }
  })

  it('lets two valid days win over a preset left in the same URL', () => {
    expect(timeRangeFromSearch({ range: '7d', from: '2026-11-01', to: '2026-11-10' })).toEqual(firstTen)
    expect(timeRangeFromSearch({ range: '7d', from: 'nope', to: '2026-11-10' })).toEqual(week)
  })
})

describe('toListRange', () => {
  const now = Date.parse('2026-11-11T09:00:00Z')

  it('maps a preset to a rolling window that ends now', () => {
    expect(toListRange({ kind: 'preset', preset: '15m' }, { timeZone: 'UTC', now })).toEqual({
      start: '2026-11-11T08:45:00.000Z',
      end: '2026-11-11T09:00:00.000Z',
    })
  })

  it('runs custom days midnight to midnight in the display zone, the last day included', () => {
    expect(toListRange(firstTen, { timeZone: 'Asia/Riyadh', now })).toEqual({
      start: '2026-10-31T21:00:00.000Z',
      end: '2026-11-10T21:00:00.000Z',
    })
  })

  it('bounds a day that changes the clocks by its real midnights, 23 or 25 hours apart', () => {
    // Cairo moves its clocks at midnight: 24 April 2026 starts at 01:00, and 29 October ends twice.
    function hours(day: string) {
      const { start, end } = toListRange({ kind: 'custom', start: day, end: day }, { timeZone: 'Africa/Cairo', now })

      return [start, (Date.parse(end) - Date.parse(start)) / h]
    }

    expect(hours('2026-04-24')).toEqual(['2026-04-23T22:00:00.000Z', 23])
    expect(hours('2026-10-29')).toEqual(['2026-10-28T21:00:00.000Z', 25])
    expect(hours('2026-10-30')).toEqual(['2026-10-29T22:00:00.000Z', 24])
  })

  it('throws on an unknown preset rather than reading an empty window', () => {
    expect(() => toListRange({ kind: 'preset', preset: '90d' }, { timeZone: 'UTC' })).toThrow('90d')
  })
})

describe('toCustomTimeRange', () => {
  it('turns a preset into the days it covers, counted in the display zone', () => {
    // 22:30 UTC on the 10th is already the 11th in Riyadh.
    const now = Date.parse('2026-11-10T22:30:00Z')

    expect(toCustomTimeRange(week, { timeZone: 'Asia/Riyadh', now })).toEqual({ kind: 'custom', start: '2026-11-04', end: '2026-11-11' })
    expect(toCustomTimeRange(week, { timeZone: 'UTC', now })).toEqual({ kind: 'custom', start: '2026-11-03', end: '2026-11-10' })
  })

  it('leaves a custom range alone', () => {
    expect(toCustomTimeRange(firstTen, { timeZone: 'UTC' })).toBe(firstTen)
  })
})

describe('calendar days', () => {
  it('reads an ISO day and refuses anything else', () => {
    expect(parseCalendarDay('2026-11-18')?.toString()).toBe('2026-11-18')

    for (const text of ['2026-02-30', '18/11/2026', '2026-11-18T09:00', 'tomorrow', '']) {
      expect(parseCalendarDay(text)).toBeNull()
    }
  })

  it('names the day an instant falls on in the zone, not the machine’s', () => {
    expect(calendarDayOf(Date.parse('2026-11-10T22:30:00Z'), 'Asia/Riyadh')).toBe('2026-11-11')
  })

  it('counts to the zone’s next midnight, through days that daylight saving shortens or lengthens', () => {
    const nineUtc = Date.parse('2026-11-11T09:00:00Z')

    expect(msUntilNextDay(nineUtc, 'UTC')).toBe(15 * h)
    expect(msUntilNextDay(nineUtc, 'Asia/Riyadh')).toBe(12 * h)
    expect(msUntilNextDay(Date.parse('2026-04-23T22:00:00Z'), 'Africa/Cairo')).toBe(23 * h)
    expect(msUntilNextDay(Date.parse('2026-10-28T21:00:00Z'), 'Africa/Cairo')).toBe(25 * h)
    expect(msUntilNextDay(Date.parse('2026-11-11T23:59:59.999Z'), 'UTC')).toBe(1)
  })
})
