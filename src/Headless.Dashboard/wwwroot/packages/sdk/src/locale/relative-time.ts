import { useCallback, useSyncExternalStore } from 'react'
import { useLocale } from './locale.tsx'

export type RelativeTimeUnit = 'second' | 'minute' | 'hour' | 'day' | 'month' | 'year'

/** `long` reads `4 minutes ago`, `short` reads `4 min. ago`, and `narrow` reads `4m ago`. */
export type RelativeTimeStyle = 'long' | 'short' | 'narrow'

// Largest first. A month is 30 days and a year 365: the text is a rough age, and the exact time sits beside it.
const units: readonly { unit: RelativeTimeUnit; ms: number }[] = [
  { unit: 'year', ms: 365 * 86_400_000 },
  { unit: 'month', ms: 30 * 86_400_000 },
  { unit: 'day', ms: 86_400_000 },
  { unit: 'hour', ms: 3_600_000 },
  { unit: 'minute', ms: 60_000 },
  { unit: 'second', ms: 1_000 },
]
const second = units[units.length - 1]!

function unitFor(distance: number) {
  return units.find((item) => distance >= item.ms) ?? second
}

/**
 * How far `at` is from `now` in the largest unit that fits, negative in the past. Whole units counted toward zero, so
 * 5 min 59 s ago is 5 minutes ago: a rounded age would call a run that started 90 s ago `2 min. ago`.
 */
export function relativeTimeParts(at: number, now: number): { value: number; unit: RelativeTimeUnit } {
  const { unit, ms } = unitFor(Math.abs(at - now))

  // `|| 0` turns the -0 of a moment under a second old into 0.
  return { value: Math.trunc((at - now) / ms) || 0, unit }
}

/** Milliseconds from `now` until `relativeTimeParts(at, …)` next gives a different answer. */
export function msUntilRelativeChange(at: number, now: number): number {
  const distance = Math.abs(at - now)
  const { ms } = unitFor(distance)

  if (at > now) {
    // The last second before a moment already reads as zero, and so does the first one after it.
    if (distance < second.ms) {
      return distance + second.ms
    }

    // A moment still ahead counts down: its value drops just after the distance passes a whole unit.
    return (distance % ms) + 1
  }

  const nextCount = ms - (distance % ms)
  // 12 months is 360 days, so the step from months to years falls between two month counts.
  const larger = units.findLast((item) => item.ms > distance)

  return larger ? Math.min(nextCount, larger.ms - distance) : nextCount
}

const formats = new Map<string, Intl.RelativeTimeFormat>()

function relativeFormat(locale: string, style: RelativeTimeStyle, numeric: 'always' | 'auto') {
  const key = `${locale}|${style}|${numeric}`
  let format = formats.get(key)

  if (!format) {
    format = new Intl.RelativeTimeFormat(locale, { style, numeric })
    formats.set(key, format)
  }

  return format
}

/**
 * `4 min. ago`, `in 12 sec.`, or `now`, in the locale's own words. Always numeric: `1 day ago`, never `yesterday`,
 * whose day boundary would be the browser's and not the display time zone's. A string that is not a time comes back
 * as it is, so a fault shows instead of an empty cell.
 */
export function formatRelativeTime(at: number | string, now: number, locale: string, style: RelativeTimeStyle = 'short') {
  const time = typeof at === 'number' ? at : Date.parse(at)

  if (Number.isNaN(time)) {
    return String(at)
  }

  const { value, unit } = relativeTimeParts(time, now)

  // `0 sec. ago` reads as a fault; every locale has a word for this moment.
  return value === 0
    ? relativeFormat(locale, style, 'auto').format(0, 'second')
    : relativeFormat(locale, style, 'always').format(value, unit)
}

type Timer = ReturnType<typeof setTimeout>

export interface RelativeClockEnv {
  now: () => number
  setTimer: (run: () => void, ms: number) => Timer
  clearTimer: (timer: Timer) => void
}

// setTimeout fires at once for a delay past 2^31 ms, so a far-off change is checked again daily.
const longestWait = 86_400_000

/**
 * One timer for every relative time on the page. It sleeps until the first watched time is due to read differently,
 * then tells every watcher, so a list of day-old rows wakes once an hour and a page without times never.
 */
export function createRelativeClock(
  env: RelativeClockEnv = {
    // Read through the globals on each call, so a test's fake timers apply to a clock made before they were installed.
    now: () => Date.now(),
    setTimer: (run, ms) => setTimeout(run, ms),
    clearTimer: (timer) => clearTimeout(timer),
  },
) {
  const watchers = new Set<{ at: number; notify: () => void }>()
  let current = env.now()
  let timer: Timer | undefined

  function schedule() {
    if (timer !== undefined) {
      env.clearTimer(timer)
    }

    timer = undefined

    if (watchers.size === 0) {
      return
    }

    let wait = longestWait

    for (const watcher of watchers) {
      wait = Math.min(wait, msUntilRelativeChange(watcher.at, current))
    }

    timer = env.setTimer(tick, wait)
  }

  function tick() {
    timer = undefined
    current = env.now()

    for (const watcher of [...watchers]) {
      watcher.notify()
    }

    schedule()
  }

  return {
    /**
     * The time every watcher is shown against. It moves only when the timer fires or a watcher joins, so two reads in
     * one render agree; with nobody watching it is the real time.
     */
    now(): number {
      if (watchers.size === 0) {
        current = env.now()
      }

      return current
    },
    /** Calls `notify` whenever the text for `at` may have changed. Returns the unsubscribe. */
    watch(at: number, notify: () => void): () => void {
      const watcher = { at, notify }

      watchers.add(watcher)
      // The timer was set for the earlier watchers; none of them is due before it, so moving the time forward here
      // changes no text of theirs.
      current = env.now()
      schedule()

      return () => {
        watchers.delete(watcher)
        schedule()
      }
    },
  }
}

/** The clock every relative time on the page shares. */
export const relativeClock = createRelativeClock()

/**
 * `at` as relative text in the current locale, re-read exactly when the text would change, from the page's one shared
 * timer. The text, not the time, is the snapshot, so React sees no change between two reads that read the same.
 */
export function useRelativeTime(at: number | string, style: RelativeTimeStyle = 'short'): string {
  const { locale } = useLocale()
  const time = typeof at === 'number' ? at : Date.parse(at)
  const known = !Number.isNaN(time)
  const subscribe = useCallback(
    (notify: () => void) => (known ? relativeClock.watch(time, notify) : () => undefined),
    [time, known],
  )
  const read = () => formatRelativeTime(at, relativeClock.now(), locale, style)

  return useSyncExternalStore(subscribe, read, read)
}
