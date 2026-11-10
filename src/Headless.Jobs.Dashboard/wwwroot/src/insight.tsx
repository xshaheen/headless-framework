import { useDashboard } from '@headless/dashboard-sdk'
import { Chart, type ChartSeries } from '@headless/dashboard-sdk/charts'
import { LoadError, SegmentedControl, Skeleton, Text } from '@headless/dashboard-sdk/components'
import type { CappedCount, DashboardApi } from '@headless/dashboard-sdk/data'
import { DateRangeField } from '@headless/dashboard-sdk/date-range'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { useStreamEvent, useTopics } from '@headless/dashboard-sdk/stream'
import type { ListRange } from '@headless/dashboard-sdk/table'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { runsTopic, statusLabel, type RunStatus } from './runs.ts'

/** One local day of the series; `counts` is `null` when the store could not read that day. */
export interface DailyBucket {
  date: string
  start: string
  end: string
  counts: Readonly<Record<RunStatus, number>> | null
}

/** Runs per status for each local day of a range, in the zone the server cut the days in. */
export interface DailySeries {
  zone: string
  from: string
  to: string
  asOf: string
  /** Whether every day was read. */
  exact: boolean
  days: DailyBucket[]
}

/** A range of local days, both ends included, as `yyyy-MM-dd`. */
export interface DayRange {
  from: string
  to: string
}

export function fetchDailySeries(api: DashboardApi, range: DayRange, zone: string, cronJobId?: string) {
  return api.request<DailySeries>({
    method: 'GET',
    path: 'jobs/series/daily',
    query: { from: range.from, to: range.to, zone, ...(cronJobId ? { cronJobId } : {}) },
  })
}

const dayFormats = new Map<string, Intl.DateTimeFormat>()

/** The local date of `at` in `zone`, as `yyyy-MM-dd`; `en-CA` writes dates in that order. */
export function localDate(at: number, zone: string) {
  let format = dayFormats.get(zone)

  if (!format) {
    format = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' })
    dayFormats.set(zone, format)
  }

  return format.format(at)
}

/** A calendar date `days` after `date`; the arithmetic runs on the date alone, so no zone shifts it. */
export function addDays(date: string, days: number) {
  return new Date(Date.parse(`${date}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10)
}

type Preset = 'around' | 'last-30' | 'last-90' | 'custom'

// A finishing chain or a bulk requeue sends many run changes at once; one read after they settle covers them all.
const SERIES_REREAD_DELAY_MS = 1000

const presets: readonly { value: Preset; label: string }[] = [
  { value: 'around', label: 'Last 7 and next 7 days' },
  { value: 'last-30', label: 'Last 30 days' },
  { value: 'last-90', label: 'Last 90 days' },
  { value: 'custom', label: 'Custom' },
]

/** The preset's days around `today`; "last 7 days" ends with today, and the next 7 follow it. */
function presetRange(preset: Exclude<Preset, 'custom'>, today: string): DayRange {
  switch (preset) {
    case 'around':
      return { from: addDays(today, -6), to: addDays(today, 7) }
    case 'last-30':
      return { from: addDays(today, -29), to: today }
    case 'last-90':
      return { from: addDays(today, -89), to: today }
  }
}

/** The picker's instants as the days they cover: its end is the midnight after the last day. */
function pickedDays(range: ListRange, zone: string): DayRange {
  return { from: localDate(Date.parse(range.start), zone), to: localDate(Date.parse(range.end) - 1, zone) }
}

const daySeries = [
  { name: statusLabel('Succeeded'), tone: 'ok', statuses: ['Succeeded', 'DueDone'] },
  { name: statusLabel('Failed'), tone: 'fail', statuses: ['Failed'] },
  { name: statusLabel('InProgress'), tone: 'proc', statuses: ['InProgress'] },
  { name: statusLabel('Queued'), tone: 'enq', statuses: ['Queued'] },
  { name: statusLabel('Idle'), tone: 'sched', statuses: ['Idle'] },
  { name: 'Cancelled or skipped', tone: 'off', statuses: ['Cancelled', 'Skipped'] },
] as const satisfies readonly { name: string; tone: ChartSeries['tone']; statuses: readonly RunStatus[] }[]

/** One series per status group, a day the store could not read staying `null` so the chart leaves a gap. */
function seriesOf(days: readonly DailyBucket[]): ChartSeries[] {
  return daySeries.map((item) => ({
    name: item.name,
    tone: item.tone,
    points: days.map((day) =>
      day.counts ? item.statuses.reduce((sum, status) => sum + (day.counts![status] ?? 0), 0) : null,
    ),
  }))
}

const hours12 = 12 * 3_600_000

/** Today's bucket, when the range holds it. */
function presentDay(days: readonly DailyBucket[], today: string) {
  const index = days.findIndex((day) => day.date === today)

  return index === -1 ? undefined : { index, label: 'Today' }
}

/**
 * Runs per day by status over a chosen range, in the display time zone: presets plus a custom range, defaulting to the
 * last 7 days and the next 7, where future days count the runs scheduled for them. A day the server could not read is
 * a gap, never zero. `control` goes where the caller shows a heading's control; `body` holds the chart.
 */
export function useRunsPerDay({ cronJobId, unit, unitOne }: { cronJobId?: string; unit: string; unitOne: string }): {
  control: ReactNode
  body: ReactNode
} {
  const { api } = useDashboard()
  const format = useFormat()
  const zone = format.timeZone
  const [preset, setPreset] = useState<Preset>('around')
  const [custom, setCustom] = useState<DayRange>()
  const today = localDate(Date.now(), zone)
  const range = preset === 'custom' ? custom : presetRange(preset, today)
  const series = useQuery({
    queryKey: ['jobs', 'series', 'daily', cronJobId ?? null, range?.from, range?.to, zone],
    queryFn: () => fetchDailySeries(api, range!, zone, cronJobId),
    enabled: range !== undefined,
  })
  const client = useQueryClient()
  const reread = useRef<ReturnType<typeof setTimeout>>(undefined)

  // A run change can move any day's counts, and the change carries no day to patch, so the series is read again once
  // a burst of changes settles rather than once per change.
  useTopics([runsTopic])
  useStreamEvent('change', (change) => {
    if (change.topic === runsTopic) {
      clearTimeout(reread.current)
      reread.current = setTimeout(
        () => void client.invalidateQueries({ queryKey: ['jobs', 'series', 'daily'] }),
        SERIES_REREAD_DELAY_MS,
      )
    }
  })
  useEffect(() => () => clearTimeout(reread.current), [])

  const control = <SegmentedControl label="Days shown" options={presets} value={preset} onChange={setPreset} />
  const picker =
    preset === 'custom' ? (
      <DateRangeField label="Days" onChange={(picked) => setCustom(picked ? pickedDays(picked, zone) : undefined)} />
    ) : null
  const data = series.data

  const chart = !range ? (
    <Text as="p" tone="hint">
      Choose the days to show.
    </Text>
  ) : series.isError ? (
    <LoadError message={series.error.message} onRetry={() => void series.refetch()} />
  ) : !data ? (
    <Skeleton label="Loading the runs per day" rows={3} />
  ) : data.days.length === 0 ? (
    <Text as="p" tone="hint">
      No days in this range.
    </Text>
  ) : (
    <Chart
      kind="stacked-bar"
      label={cronJobId ? 'Occurrences per day by status' : 'Runs per day by status'}
      unit={unit}
      unitOne={unitOne}
      asOf={data.asOf}
      zone={data.zone}
      // Each bar is labelled from the first day's start plus whole days; starting at noon keeps every label on its own
      // date when a daylight-saving change makes a day 23 or 25 hours long.
      start={Date.parse(data.days[0]!.start) + hours12}
      intervalSeconds={86_400}
      series={seriesOf(data.days)}
      // Days after today count the time jobs scheduled for them; a range that does not hold today marks nothing.
      present={presentDay(data.days, today)}
    />
  )

  return {
    control,
    body: (
      <>
        {picker}
        {chart}
      </>
    ),
  }
}

const distributionParts = [
  { name: statusLabel('Succeeded'), tone: 'ok', statuses: ['Succeeded', 'DueDone'] },
  { name: statusLabel('Failed'), tone: 'fail', statuses: ['Failed'] },
  { name: statusLabel('InProgress'), tone: 'proc', statuses: ['InProgress'] },
  { name: statusLabel('Queued'), tone: 'enq', statuses: ['Queued'] },
  { name: statusLabel('Idle'), tone: 'sched', statuses: ['Idle'] },
  { name: 'Cancelled or skipped', tone: 'off', statuses: ['Cancelled', 'Skipped'] },
] as const satisfies readonly { name: string; tone: ChartSeries['tone']; statuses: readonly RunStatus[] }[]

/** A part's count from its statuses: capped when any of them is, since their sum is then a floor too. */
function partCount(counts: Readonly<Record<string, CappedCount>>, statuses: readonly RunStatus[]): CappedCount {
  return statuses.reduce<CappedCount>(
    (sum, status) => ({
      value: sum.value + (counts[status]?.value ?? 0),
      capped: sum.capped || counts[status]?.capped === true,
    }),
    { value: 0, capped: false },
  )
}

/**
 * The stored runs by status, as the sidebar counts them, with each part's share and a donut. A capped count, a module
 * that did not answer, or a status the answer lacks makes the counts partial: then the chart lists the counts alone,
 * because a share of a partial count is a wrong number.
 */
export function StatusDistribution({
  counts,
  stale,
  fetchedAt,
}: {
  counts: Readonly<Record<string, CappedCount>>
  stale: boolean
  fetchedAt: number
}) {
  const format = useFormat()
  const complete =
    !stale && distributionParts.every((part) => part.statuses.every((status) => counts[status] !== undefined))

  return (
    <Chart
      kind="donut"
      label="Runs by status"
      unit="runs"
      unitOne="run"
      // The counts cover every stored run, not a window, so they carry no range, only when they were fetched.
      asOf={fetchedAt}
      zone={format.timeZone}
      complete={complete}
      parts={distributionParts.map((part) => ({
        name: part.name,
        tone: part.tone,
        count: partCount(counts, part.statuses),
      }))}
      empty="No stored runs"
    />
  )
}
