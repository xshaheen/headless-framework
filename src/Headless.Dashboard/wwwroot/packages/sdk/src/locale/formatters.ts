import type { CappedCount } from '../data/counts.ts'

/** Anything a formatter reads as an instant: a `Date`, epoch milliseconds, or an ISO string from the server. */
export type TimeInput = Date | number | string

export interface RelativeTimeOptions {
  /** The instant `at` is measured from; the current time by default. */
  now?: number
  /** `short` reads `4 min. ago`; `long` reads `4 minutes ago`. */
  style?: 'long' | 'short'
}

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
  /** `4 min. ago`, `in 12 sec.`. The exact time goes in a tooltip beside it. */
  relativeTime: (at: TimeInput, options?: RelativeTimeOptions) => string
  /** The exact time with its zone and offset, in the display time zone. */
  exactTime: (at: TimeInput) => string
  /**
   * The exact time in a zone the caller names, such as the scheduler's for a cron field, whatever the display time
   * zone is.
   */
  zonedTime: (at: TimeInput, timeZone: string) => string
}

const steps: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 30],
  ['month', 12],
]

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
  const relatives = new Map<string, Intl.RelativeTimeFormat>()
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

  function relativeFormat(style: 'long' | 'short') {
    let format = relatives.get(style)

    if (!format) {
      format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style })
      relatives.set(style, format)
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
    relativeTime: (at, options = {}) => {
      const time = toEpoch(at)

      if (Number.isNaN(time)) {
        return String(at)
      }

      const format = relativeFormat(options.style ?? 'long')
      let value = (time - (options.now ?? Date.now())) / 1_000

      for (const [unit, size] of steps) {
        if (Math.abs(value) < size) {
          return format.format(Math.round(value), unit)
        }

        value /= size
      }

      return format.format(Math.round(value), 'year')
    },
    exactTime: (at) => zonedTime(at, timeZone),
    zonedTime,
  }
}
