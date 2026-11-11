import type { ListRange } from '../data/keyset.ts'
import { calendarDayOf, parseCalendarDay } from './calendar-date.ts'

/** A rolling window that ends now: the last `amount` minutes, hours, or days. */
export interface TimeRangePreset {
  /** What the URL holds (`24h`). `custom` is taken by the custom range. */
  id: string
  amount: number
  unit: 'minute' | 'hour' | 'day'
  /** Replaces the label the locale gives (`24 hr`). */
  label?: string
}

export const timeRangePresets: readonly TimeRangePreset[] = [
  { id: '15m', amount: 15, unit: 'minute' },
  { id: '1h', amount: 1, unit: 'hour' },
  { id: '24h', amount: 24, unit: 'hour' },
  { id: '7d', amount: 7, unit: 'day' },
  { id: '30d', amount: 30, unit: 'day' },
]

/**
 * A preset window, or two ISO days (`2026-11-01`), both included. Days and not instants, so a shared link means the
 * same days to an operator who reads it in another display time zone.
 */
export type TimeRange = { kind: 'preset'; preset: string } | { kind: 'custom'; start: string; end: string }

export type CustomTimeRange = Extract<TimeRange, { kind: 'custom' }>

/** The three search params a range takes. A key that does not apply is `undefined`, which removes it. */
export interface TimeRangeSearch {
  range: string | undefined
  from: string | undefined
  to: string | undefined
}

export interface TimeRangeSearchOptions {
  presets?: readonly TimeRangePreset[]
  /** The range a URL without the params means, which writes none; no range (the whole list) when left out. */
  defaultRange?: TimeRange
}

const unitMs = { minute: 60_000, hour: 3_600_000, day: 86_400_000 } as const

function sameRange(a: TimeRange | undefined, b: TimeRange | undefined) {
  if (a === undefined || b === undefined) {
    return a === b
  }

  return a.kind === 'preset'
    ? b.kind === 'preset' && a.preset === b.preset
    : b.kind === 'custom' && a.start === b.start && a.end === b.end
}

/** The search params for a range. The default range writes none, so the plain URL means it. */
export function timeRangeToSearch(range: TimeRange | undefined, defaultRange?: TimeRange): TimeRangeSearch {
  if (range === undefined || sameRange(range, defaultRange)) {
    return { range: undefined, from: undefined, to: undefined }
  }

  return range.kind === 'preset'
    ? { range: range.preset, from: undefined, to: undefined }
    : { range: undefined, from: range.start, to: range.end }
}

/**
 * Reads a range back from search params. People type and paste URLs, so anything that is not a known preset or a real
 * pair of days in order reads as the default instead of throwing. Two valid days win over a preset in the same URL.
 */
export function timeRangeFromSearch(
  search: { readonly range?: unknown; readonly from?: unknown; readonly to?: unknown },
  { presets = timeRangePresets, defaultRange }: TimeRangeSearchOptions = {},
): TimeRange | undefined {
  const { range, from, to } = search

  if (typeof from === 'string' && typeof to === 'string') {
    const start = parseCalendarDay(from)
    const end = parseCalendarDay(to)

    if (start && end && start.compare(end) <= 0) {
      return { kind: 'custom', start: start.toString(), end: end.toString() }
    }
  }

  if (typeof range === 'string' && presets.some((preset) => preset.id === range)) {
    return { kind: 'preset', preset: range }
  }

  return defaultRange
}

export interface ResolveTimeRangeOptions {
  /** The IANA zone whose midnights bound a custom range: the display time zone. */
  timeZone: string
  now?: number
  presets?: readonly TimeRangePreset[]
}

/**
 * The instants a range covers as a list's `range`: `start` included, `end` excluded, in UTC ISO. A preset ends now; a
 * custom range runs from its first day's midnight to the midnight after its last day, in `timeZone`, so a day that
 * moves the clocks is its real 23 or 25 hours.
 */
export function toListRange(
  range: TimeRange,
  { timeZone, now = Date.now(), presets = timeRangePresets }: ResolveTimeRangeOptions,
): ListRange {
  if (range.kind === 'preset') {
    const preset = presets.find((item) => item.id === range.preset)

    if (!preset) {
      throw new Error(`Unknown time range preset '${range.preset}'.`)
    }

    return { start: new Date(now - preset.amount * unitMs[preset.unit]).toISOString(), end: new Date(now).toISOString() }
  }

  const start = parseCalendarDay(range.start)
  const end = parseCalendarDay(range.end)

  if (!start || !end) {
    throw new Error(`Not a pair of ISO days: ${range.start}, ${range.end}.`)
  }

  return { start: start.toDate(timeZone).toISOString(), end: end.add({ days: 1 }).toDate(timeZone).toISOString() }
}

/**
 * The days a range touches, for the date fields when the operator switches a preset to Custom: `7d` becomes the day it
 * started through today, counted in `timeZone`.
 */
export function toCustomTimeRange(range: TimeRange, options: ResolveTimeRangeOptions): CustomTimeRange {
  if (range.kind === 'custom') {
    return range
  }

  const { start, end } = toListRange(range, options)

  return { kind: 'custom', start: calendarDayOf(Date.parse(start), options.timeZone), end: calendarDayOf(Date.parse(end), options.timeZone) }
}
