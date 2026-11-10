import type { CappedCount } from '../data/counts.ts'
import type { DashboardStateRole } from '../define-module.ts'
import { toEpoch, type Formatters, type TimeInput } from '../locale/formatters.ts'

/** A series colour: one of the six state roles, the brand accent, or the muted text tone. */
export type ChartTone = DashboardStateRole | 'accent' | 'muted'

export interface ChartSeries {
  /** What the series counts: `Published`. */
  name: string
  tone: ChartTone
  /** One value per bucket, oldest first, as the server serves it; `null` where the bucket was not measured. */
  points: readonly (number | null)[]
}

interface ChartBase {
  /** What the chart shows: `Throughput`. It opens the image's spoken sentence and labels the list. */
  label: string
  /** What one value counts, plural: `runs`, `messages/s`. */
  unit: string
  /** The unit for a value of exactly one, `run`; without it a value of one reads with the plural. */
  unitOne?: string
  /** When the server read the data. */
  asOf: TimeInput
  /** The IANA zone the server cut the buckets in, so `09:00` means the server's 09:00. */
  zone: string
}

export interface TimeSeriesChartProps extends ChartBase {
  /** `stacked-bar` stacks the series in order, the first at the bottom; `line` and `area` overlay them. */
  kind: 'line' | 'area' | 'stacked-bar'
  series: readonly ChartSeries[]
  /** When the first bucket starts. */
  start: TimeInput
  /** How long one bucket is, in seconds. */
  intervalSeconds: number
  /**
   * The bucket that holds the present, for a chart that also counts what is scheduled: the axis marks it with `label`
   * (`Today`), and every later bucket counts work not yet run, so its bars draw outlined and its list row says so.
   */
  present?: { index: number; label: string }
}

export interface DonutPart {
  /** What the part counts: `Failed`. */
  name: string
  tone: ChartTone
  count: CappedCount
}

export interface DonutChartProps extends Omit<ChartBase, 'asOf'> {
  kind: 'donut'
  /** When the server read the counts; for counts with no range of their own, when they were fetched. */
  asOf?: TimeInput
  parts: readonly DonutPart[]
  /**
   * Whether the server counted every item in the range. A share is shown only when this holds and no count is capped;
   * otherwise the chart becomes the list of counts, because a share of a partial count is a wrong number.
   */
  complete: boolean
  /** The range the parts cover; without one, the parts count every item the server holds. */
  start?: TimeInput
  end?: TimeInput
  /** What the chart says when every part counts zero; `No {unit} in this range` by default. */
  empty?: string
}

/** The one chart API: the kind picks the drawing, and every kind has the same label, tooltip, and list. */
export type ChartProps = TimeSeriesChartProps | DonutChartProps

/** The CSS variable a series paints with, set on the chart's container from its tone. */
export function seriesColour(index: number) {
  return `var(--series-${index})`
}

export function seriesVariables(tones: readonly ChartTone[]) {
  return Object.fromEntries(tones.map((tone, index) => [`--series-${index}`, `var(--${tone})`]))
}

const dateFormats = new Map<string, Intl.DateTimeFormat>()
const numberFormats = new Map<string, Intl.NumberFormat>()
const listFormats = new Map<string, Intl.ListFormat>()

function cached<T>(cache: Map<string, T>, locale: string, options: object, create: () => T) {
  const key = `${locale}|${JSON.stringify(options)}`
  let format = cache.get(key)

  if (!format) {
    format = create()
    cache.set(key, format)
  }

  return format
}

/**
 * Formats a bucket's start in the server's zone, as precisely as the interval needs: seconds under a minute, the
 * date once the chart spans more than a day.
 */
export function bucketFormat(locale: string, zone: string, intervalSeconds: number, spanSeconds: number) {
  const options: Intl.DateTimeFormatOptions = {
    month: spanSeconds >= 86_400 ? 'short' : undefined,
    day: spanSeconds >= 86_400 ? 'numeric' : undefined,
    hour: intervalSeconds < 86_400 ? '2-digit' : undefined,
    minute: intervalSeconds < 86_400 ? '2-digit' : undefined,
    second: intervalSeconds < 60 ? '2-digit' : undefined,
    hourCycle: 'h23',
    timeZone: zone,
  }
  const format = cached(dateFormats, locale, options, () => new Intl.DateTimeFormat(locale, options))

  return (at: number) => format.format(at)
}

function intervalPhrase(locale: string, seconds: number) {
  const [unit, size] =
    seconds % 86_400 === 0 ? (['day', 86_400] as const)
    : seconds % 3_600 === 0 ? (['hour', 3_600] as const)
    : seconds % 60 === 0 ? (['minute', 60] as const)
    : (['second', 1] as const)

  const options: Intl.NumberFormatOptions = { style: 'unit', unit, unitDisplay: 'long' }

  return cached(numberFormats, locale, options, () => new Intl.NumberFormat(locale, options)).format(seconds / size)
}

export interface BucketRow {
  /** The bucket's start, as the list and the tooltip name it. */
  label: string
  /** The present's label on its bucket, or `scheduled` on a later one, which the list and the tooltip add to `label`. */
  mark?: string
  /** Whether the bucket lies after the present, so it counts scheduled work. */
  future: boolean
  /** One text per series, in the series' order: the exact value with its unit, or that it was not measured. */
  values: string[]
}

/** A bucket's name with its mark, as the list and the tooltip show it: `Nov 12 · scheduled`. */
export function bucketName(row: Pick<BucketRow, 'label' | 'mark'>) {
  return row.mark ? `${row.label} · ${row.mark}` : row.label
}

/** The text of one served value, shared by the tooltip and the list so the two can never disagree. */
export function valueText(format: Formatters, value: number | null, unit: Pick<ChartBase, 'unit' | 'unitOne'>) {
  return value === null ? 'not measured' : `${format.number(value)} ${unitFor(value, unit)}`
}

/** The unit word for `value`: the singular for exactly one when the chart names one, else the plural. */
export function unitFor(value: number, unit: Pick<ChartBase, 'unit' | 'unitOne'>) {
  return value === 1 && unit.unitOne ? unit.unitOne : unit.unit
}

/** One row per bucket, which both the tooltip and the list render. */
export function bucketRows(props: TimeSeriesChartProps, format: Formatters): BucketRow[] {
  const count = Math.max(0, ...props.series.map((item) => item.points.length))
  const start = toEpoch(props.start)
  const label = bucketFormat(format.locale, props.zone, props.intervalSeconds, count * props.intervalSeconds)

  const present = props.present

  return Array.from({ length: count }, (_, index) => ({
    label: label(start + index * props.intervalSeconds * 1_000),
    mark: present?.index === index ? present.label : present && index > present.index ? 'scheduled' : undefined,
    future: present !== undefined && index > present.index,
    values: props.series.map((item) => valueText(format, item.points[index] ?? null, props)),
  }))
}

function names(locale: string, items: readonly { name: string }[]) {
  const options: Intl.ListFormatOptions = { type: 'conjunction' }

  return cached(listFormats, locale, options, () => new Intl.ListFormat(locale, options)).format(items.map((item) => item.name))
}

/** The image's spoken sentence: what it shows, which series, the buckets' range and zone, and when it was read. */
export function timeSeriesSentence(props: TimeSeriesChartProps, rows: readonly BucketRow[], format: Formatters) {
  const range = rows.length > 0 ? `from ${rows[0]!.label} to ${rows.at(-1)!.label} ${props.zone}` : 'with no buckets'
  const gaps = props.series.some((item) => item.points.some((point) => point === null))
    ? '; unmeasured buckets are gaps'
    : ''
  const present = props.present && rows[props.present.index]
  const later = props.present && props.present.index < rows.length - 1 ? ', later buckets are scheduled and drawn outlined' : ''
  const scheduled = present ? `; ${props.present!.label} is ${present.label}${later}` : ''

  return `${props.label}: ${names(format.locale, props.series)} in ${props.unit}, every ${intervalPhrase(format.locale, props.intervalSeconds)}, ${range}${gaps}${scheduled}; as of ${format.zonedTime(props.asOf, props.zone)}.`
}

export interface PartRow {
  name: string
  count: string
  /** `42.1 %` when every count is exact and complete, otherwise `null`. */
  share: string | null
}

/** One row per part. Shares appear only when the counts behind them are exact and complete, and some count is not zero. */
export function partRows(props: DonutChartProps, format: Formatters): PartRow[] {
  const total = props.parts.reduce((sum, part) => sum + part.count.value, 0)
  const shares = props.complete && total > 0 && props.parts.every((part) => !part.count.capped)

  return props.parts.map((part) => ({
    name: part.name,
    count: `${format.count(part.count)} ${part.count.capped ? props.unit : unitFor(part.count.value, props)}`,
    share: shares ? format.number(part.count.value / total, { style: 'percent', maximumFractionDigits: 1 }) : null,
  }))
}

/** The range the parts cover, or `all {unit}` when they have none. */
export function donutRange(props: DonutChartProps, format: Formatters) {
  if (props.start === undefined || props.end === undefined) {
    return `all ${props.unit}`
  }

  const start = toEpoch(props.start)
  const end = toEpoch(props.end)
  const label = bucketFormat(format.locale, props.zone, 60, (end - start) / 1_000)

  return `from ${label(start)} to ${label(end)} ${props.zone}`
}

export function donutSentence(props: DonutChartProps, rows: readonly PartRow[], format: Formatters) {
  const parts = rows.map((row) => `${row.name} ${row.count}${row.share ? `, ${row.share}` : ''}`).join('; ')

  const asOf = props.asOf === undefined ? '' : `; as of ${format.zonedTime(props.asOf, props.zone)}`

  return `${props.label}: ${parts}, ${donutRange(props, format)}${asOf}.`
}
