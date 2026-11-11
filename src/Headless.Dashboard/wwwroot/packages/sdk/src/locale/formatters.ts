import type { CappedCount } from '../data/counts.ts'

/** Anything a formatter reads as an instant: a `Date`, epoch milliseconds, or an ISO string from the server. */
export type TimeInput = Date | number | string

/** Every text formatter a view needs, bound to one locale and one display time zone. */
export interface Formatters {
  readonly locale: string
  readonly timeZone: string
  /** A number in the locale: `1,234.5`. */
  number: (value: number, options?: Intl.NumberFormatOptions) => string
  /** A count, `312`, or `10,000+` when the server capped it. */
  count: (count: CappedCount) => string
  /** A byte size: `812 B`, `70.0 KB`. */
  size: (bytes: number) => string
  /** A duration with its unit: `340 ms`, `1.2 s`, `3 min 4 s`. */
  duration: (ms: number) => string
  /** The exact time with its zone and offset, in the display time zone. */
  exactTime: (at: TimeInput) => string
  /**
   * The exact time in a zone the caller names, such as the scheduler's for a cron field, whatever the display time
   * zone is.
   */
  zonedTime: (at: TimeInput, timeZone: string) => string
}

/** Epoch milliseconds of `at`, or `NaN` when a string does not parse. */
export function toEpoch(at: TimeInput) {
  return at instanceof Date ? at.getTime() : typeof at === 'number' ? at : Date.parse(at)
}

/** Whether the runtime knows `timeZone` as an IANA zone name. */
export function isTimeZone(timeZone: string) {
  try {
    new Intl.DateTimeFormat(undefined, { timeZone })
    return true
  } catch {
    return false
  }
}

/** Builds the formatters for one locale and display time zone, creating each `Intl` formatter once. */
export function createFormatters(locale: string, timeZone: string): Formatters {
  const numbers = new Map<string, Intl.NumberFormat>()
  const exacts = new Map<string, Intl.DateTimeFormat>()

  function numberFormat(options: Intl.NumberFormatOptions = {}) {
    const key = JSON.stringify(options)
    let format = numbers.get(key)

    if (!format) {
      format = new Intl.NumberFormat(locale, options)
      numbers.set(key, format)
    }

    return format
  }

  function zonedTime(at: TimeInput, zone: string) {
    const time = toEpoch(at)

    if (Number.isNaN(time)) {
      return String(at)
    }

    let format = exacts.get(zone)

    if (!format) {
      // UTC reads as itself; any other zone shows its offset, and its name follows so the operator knows which zone.
      format = new Intl.DateTimeFormat(locale, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hourCycle: 'h23',
        timeZone: zone,
        timeZoneName: zone === 'UTC' ? 'short' : 'longOffset',
      })
      exacts.set(zone, format)
    }

    const text = format.format(time)

    return zone === 'UTC' ? text : `${text} ${zone}`
  }

  return {
    locale,
    timeZone,
    number: (value, options) => numberFormat(options).format(value),
    count: (count) => {
      const text = numberFormat().format(count.value)

      return count.capped ? `${text}+` : text
    },
    size: (bytes) =>
      bytes < 1024
        ? `${numberFormat().format(bytes)} B`
        : `${numberFormat({ minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(bytes / 1024)} KB`,
    duration: (ms) => {
      if (ms < 1_000) {
        return `${numberFormat().format(ms)} ms`
      }

      if (ms < 60_000) {
        return `${numberFormat({ maximumFractionDigits: 1 }).format(ms / 1_000)} s`
      }

      // Rounded to whole seconds before the split, so 3 min 59.6 s reads `4 min 0 s`, never `3 min 60 s`.
      const seconds = Math.round(ms / 1_000)

      return `${numberFormat().format(Math.floor(seconds / 60))} min ${numberFormat().format(seconds % 60)} s`
    },
    exactTime: (at) => zonedTime(at, timeZone),
    zonedTime,
  }
}
