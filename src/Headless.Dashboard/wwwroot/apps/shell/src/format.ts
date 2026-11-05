const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto', style: 'short' })
const exact = new Intl.DateTimeFormat('en', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
  timeZone: 'UTC',
  timeZoneName: 'short',
})
const number = new Intl.NumberFormat('en', { maximumFractionDigits: 1 })

const steps: [Intl.RelativeTimeFormatUnit, number][] = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 30],
  ['month', 12],
]

/** `4 min. ago`: a time relative to `now`. The exact time goes in a tooltip beside it. */
export function relativeTime(iso: string, now = Date.now()) {
  const time = Date.parse(iso)

  if (Number.isNaN(time)) {
    return iso
  }

  let value = (time - now) / 1_000

  for (const [unit, size] of steps) {
    if (Math.abs(value) < size) {
      return relative.format(Math.round(value), unit)
    }

    value /= size
  }

  return relative.format(Math.round(value), 'year')
}

/** The exact time with its zone, in the dashboard's display zone. */
export function exactTime(iso: string) {
  const time = Date.parse(iso)

  return Number.isNaN(time) ? iso : exact.format(time)
}

/** A figure's value with its unit: `98.2 %`, `1.2 k/min`, `312`. */
export function figureText(value: number, unit?: string | null) {
  const text = number.format(value)

  return unit ? `${text} ${unit}` : text
}

/** A figure's comparison: `−1.1 vs previous hour`, with whether the change is good news. */
export function comparisonText(value: number, comparison: { label: string; value: number; higherIsBetter?: boolean }, unit?: string | null) {
  const delta = value - comparison.value
  const sign = delta > 0 ? '+' : delta < 0 ? '−' : '±'
  const text = `${sign}${number.format(Math.abs(delta))}${unit ? ` ${unit}` : ''} vs ${comparison.label}`
  const better = delta === 0 ? undefined : (delta > 0) === (comparison.higherIsBetter ?? true)

  return { text, better }
}
