import type { CappedCount } from '../data/counts.ts'

/** Anything a formatter reads as an instant: a `Date`, epoch milliseconds, or an ISO string from the server. */
export type TimeInput = Date | number | string

const byteUnits = ['byte', 'kilobyte', 'megabyte', 'gigabyte', 'terabyte'] as const

/** Every text formatter a view needs, bound to one locale and one display time zone. */
export interface Formatters {
  readonly locale: string
  readonly timeZone: string
  /** A number in the locale: `1,234.5`. */
  number: (value: number, options?: Intl.NumberFormatOptions) => string
  /** A count, `312`, or `10,000+` when the server capped it. */
  count: (count: CappedCount) => string
  /**
   * A byte size in the largest unit that keeps it at or above one, 1,024 to the step: `812 bytes`, `70 kB`, `1.5 MB`,
   * `2.1 GB`. The unit's name comes from `Intl`, so it reads in the locale.
   */
  size: (bytes: number) => string
  /**
   * A duration in at most two units, joined the locale's way: `340 ms`, `1.2 sec`, `3 min 4 sec`, `2 hr 5 min`. A zero
   * second unit is left out (`4 min`), and every step rounds first, so it never reads `3 min 60 sec`.
   */
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
  let lists: Intl.ListFormat | undefined

  function numberFormat(options: Intl.NumberFormatOptions = {}) {
    const key = JSON.stringify(options)
    let format = numbers.get(key)

    if (!format) {
      format = new Intl.NumberFormat(locale, options)
      numbers.set(key, format)
    }

    return format
  }

  // A byte count reads in the long form (`812 bytes`): the short English form is the singular `812 byte`.
  function unitFormat(unit: string, maximumFractionDigits = 0) {
    return numberFormat({
      style: 'unit',
      unit,
      unitDisplay: unit === 'byte' ? 'long' : 'short',
      maximumFractionDigits,
    })
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
    size: (bytes) => {
      // Rounded as it would be shown before the unit is chosen, so 1,023.96 kB reads `1 MB`, never `1,024 kB`.
      let value = Math.abs(bytes)
      let at = 0

      while (at < byteUnits.length - 1 && (at === 0 ? Math.round(value) : Math.round(value * 10) / 10) >= 1024) {
        value /= 1024
        at++
      }

      const unit = byteUnits[at]!

      return unitFormat(unit, unit === 'byte' ? 0 : 1).format(Math.sign(bytes) * value)
    },
    duration: (ms) => {
      const sign = ms < 0 ? -1 : 1
      const value = Math.abs(ms)

      // Each step rounds before it checks its threshold, so 999.6 ms reads `1 sec` and 3 min 59.6 s reads `4 min`,
      // never `1,000 ms` or `3 min, 60 sec`.
      if (Math.round(value) < 1_000) {
        return unitFormat('millisecond').format(sign * Math.round(value))
      }

      const tenths = Math.round(value / 100) / 10

      if (tenths < 60) {
        return unitFormat('second', 1).format(sign * tenths)
      }

      // Below an hour the pair is minutes and seconds, from whole seconds; above, hours and minutes, from whole minutes.
      const seconds = Math.round(value / 1_000)
      const isHours = seconds >= 3_600
      const whole = isHours ? Math.round(value / 60_000) : seconds
      const parts = [unitFormat(isHours ? 'hour' : 'minute').format(sign * Math.floor(whole / 60))]

      if (whole % 60 > 0) {
        parts.push(unitFormat(isHours ? 'minute' : 'second').format(whole % 60))
      }

      return (lists ??= new Intl.ListFormat(locale, { style: 'narrow', type: 'unit' })).format(parts)
    },
    exactTime: (at) => zonedTime(at, timeZone),
    zonedTime,
  }
}
