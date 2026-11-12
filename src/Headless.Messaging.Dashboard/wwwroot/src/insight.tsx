import { useDashboard } from '@headless/dashboard-sdk'
import { Chart, type ChartSeries, type DonutPart } from '@headless/dashboard-sdk/charts'
import { Card, CardRow, LoadError, SegmentedControl, Skeleton, Text } from '@headless/dashboard-sdk/components'
import type { CappedCount } from '@headless/dashboard-sdk/data'
import { ChartIcon, PublishedIcon, ReceivedIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { useState } from 'react'
import { fetchMessages, nodeQuery, readFilter, type MessageKind } from './messages.ts'
import { useMessagingCounts, useNode, useRealtime } from './parts.tsx'

/** One store's hourly counts: one entry per hour, `null` for an hour the store did not count. */
interface HourlySeries {
  succeeded: (number | null)[]
  failed: (number | null)[]
}

export interface HourlyHistory {
  interval: string
  zone: string
  asOf: string
  hourStarts: string[]
  /** A store that did not answer has neither series, and is named in `unavailable`. */
  stores: { store: string; published?: HourlySeries | null; received?: HourlySeries | null }[]
  unavailable?: string[]
}

/**
 * The live five minutes, second by second: the rate on one chart and the consume time on the other, on the same time
 * axis, so a spike in one lines up with the other. A second the node did not measure is a gap in both.
 */
export function LiveThroughput() {
  const format = useFormat()
  const realtime = useRealtime()
  const data = realtime.data
  const first = data?.seconds[0]
  const last = data?.seconds.at(-1)

  return (
    <Card
      title="Live throughput"
      icon={ChartIcon}
      wide
      empty={
        realtime.isError ? (
          <LoadError message={realtime.error.message} onRetry={() => void realtime.refetch()} />
        ) : !data ? (
          <Skeleton label="Loading the live throughput" shape="chart" />
        ) : (
          'This node has measured no second yet.'
        )
      }
    >
      {data && first !== undefined && last !== undefined
        ? [
            <CardRow key="rate">
              <Text tone="hint">Messages per second, the last five minutes on this node</Text>
              <Chart
                kind="line"
                label="Message rate"
                unit="messages/s"
                unitOne="message/s"
                empty="No messages in the last five minutes"
                asOf={(last + 1) * 1_000}
                zone={format.timeZone}
                start={first * 1_000}
                intervalSeconds={1}
                series={[
                  { name: 'Published', tone: 'accent', points: data.publishedPerSecond },
                  { name: 'Received', tone: 'ok', points: data.consumedPerSecond },
                ]}
              />
            </CardRow>,
            <CardRow key="latency">
              <Text tone="hint">Mean time a consumer took, per second</Text>
              <Chart
                kind="line"
                label="Consume latency"
                unit="ms"
                empty="No message was consumed in the last five minutes"
                asOf={(last + 1) * 1_000}
                zone={format.timeZone}
                start={first * 1_000}
                intervalSeconds={1}
                series={[{ name: 'Consume time', tone: 'proc', points: data.consumeElapsedMs }]}
              />
            </CardRow>,
          ]
        : null}
    </Card>
  )
}

/** The node's 24 hours, cut at the server's hours; a store that did not answer is named, never counted as zero. */
function useHourly() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'hourly', node],
    queryFn: () =>
      api.request<HourlyHistory>({ method: 'GET', path: 'messaging/metrics/hourly', query: nodeQuery(node) }),
    refetchInterval: 60_000,
  })
}

/**
 * Each hour's total over the stores that answered: an hour any of them did not count is unmeasured, because a sum that
 * leaves a store out would read as a drop.
 */
export function sumHours(series: readonly HourlySeries[], field: keyof HourlySeries, hours: number) {
  return Array.from({ length: hours }, (_, index) => {
    let sum = 0

    for (const item of series) {
      const value = item[field][index]

      if (value === null || value === undefined) {
        return null
      }

      sum += value
    }

    return sum
  })
}

const kindOptions: readonly { value: MessageKind; label: string }[] = [
  { value: 'received', label: 'Received' },
  { value: 'published', label: 'Published' },
]

export function HourlyHistoryCard() {
  const hourly = useHourly()
  const [kind, setKind] = useState<MessageKind>('received')
  const data = hourly.data
  const series = (data?.stores ?? []).flatMap((store) => {
    const item = kind === 'published' ? store.published : store.received

    return item ? [item] : []
  })
  const unavailable = data?.unavailable ?? []
  const hours = data?.hourStarts.length ?? 0
  const chartSeries: ChartSeries[] = [
    { name: 'Succeeded', tone: 'ok', points: sumHours(series, 'succeeded', hours) },
    { name: 'Failed', tone: 'fail', points: sumHours(series, 'failed', hours) },
  ]

  return (
    <Card
      title="Last 24 hours"
      icon={kind === 'published' ? PublishedIcon : ReceivedIcon}
      wide
      action={<SegmentedControl label="Messages per hour of" options={kindOptions} value={kind} onChange={setKind} />}
      empty={
        hourly.isError ? (
          <LoadError message={hourly.error.message} onRetry={() => void hourly.refetch()} />
        ) : !data ? (
          <Skeleton label="Loading the last 24 hours" shape="chart" />
        ) : unavailable.length > 0 ? (
          <LoadError
            message={`No store answered, so the hours are unknown: ${unavailable.join(', ')}.`}
            onRetry={() => void hourly.refetch()}
          />
        ) : (
          `No store on this node holds ${kind} messages.`
        )
      }
    >
      {data && hours > 0 && series.length > 0 ? (
        <CardRow>
          <Text tone="hint">
            {`${kind === 'published' ? 'Published' : 'Received'} messages by the hour they were added, in their status now, ${data.zone}`}
          </Text>
          {unavailable.length > 0 ? (
            <Text tone="error" role="status">
              {`Not counted, as these stores did not answer: ${unavailable.join(', ')}.`}
            </Text>
          ) : null}
          <Chart
            kind="stacked-bar"
            label={kind === 'published' ? 'Published per hour' : 'Received per hour'}
            unit="messages"
            unitOne="message"
            asOf={data.asOf}
            zone={data.zone}
            start={data.hourStarts[0]!}
            intervalSeconds={3_600}
            series={chartSeries}
            // The last hour is still running, so its bar can still grow.
            present={{ index: hours - 1, label: 'This hour' }}
          />
        </CardRow>
      ) : null}
    </Card>
  )
}

/** The count keys each kind's donut adds up, as the sidebar serves them. */
const statusKeys: Readonly<Record<MessageKind, readonly string[]>> = {
  received: ['received.all', 'received.succeeded', 'received.failed', 'received.retrying'],
  published: [
    'published.all',
    'published.succeeded',
    'published.failed',
    'published.retrying',
    'published.delayed',
    'published.scheduled',
    'published.queued',
  ],
}

/** The URL filter each count key reads on another node, whose counts the shell does not serve. */
const keyFilters: Readonly<Record<string, string>> = {
  all: '',
  succeeded: 'status=Succeeded',
  failed: 'status=Failed&terminal=true',
  retrying: 'retrying=true',
  delayed: 'status=Delayed',
  scheduled: 'status=Scheduled',
  queued: 'status=Queued',
}

/**
 * The status counts a donut reads. The local node's come from the counts the sidebar and the strip share; another
 * node's are read from its lists, one row each, and only for the donut, so the strip on every view stays cheap.
 */
function useStatusCounts() {
  const { api } = useDashboard()
  const { node } = useNode()
  const local = useMessagingCounts()
  const peer = useQuery({
    queryKey: ['messaging', 'status-counts', node],
    enabled: node !== '',
    queryFn: async () => {
      const keys = [...statusKeys.received, ...statusKeys.published]
      const read = await Promise.all(
        keys.map(async (key) => {
          const [kind, list] = key.split('.') as [MessageKind, string]
          const page = await fetchMessages(
            api,
            kind,
            node,
            readFilter(new URLSearchParams(keyFilters[list]), kind),
            undefined,
            {},
            1,
          )

          return [key, page.count] as const
        }),
      )

      return { counts: Object.fromEntries(read) as Record<string, CappedCount>, stale: false }
    },
  })

  if (node === '') {
    return {
      query: local,
      counts: local.data?.counts,
      stale: (local.data?.stale.length ?? 0) > 0,
      fetchedAt: local.dataUpdatedAt,
    }
  }

  return { query: peer, counts: peer.data?.counts, stale: false, fetchedAt: peer.dataUpdatedAt }
}

/**
 * The parts of one kind's donut. Received rows that are neither finished nor failed (queued, scheduled, or delayed)
 * are the total less the rest, which is a count only when every count behind it is exact: otherwise the part is left
 * out and the donut is incomplete.
 */
export function statusParts(
  kind: MessageKind,
  counts: Readonly<Record<string, CappedCount | undefined>>,
): { parts: DonutPart[]; complete: boolean } {
  const read = (key: string) => counts[`${kind}.${key}`]
  const named: { name: string; tone: DonutPart['tone']; key: string }[] =
    kind === 'published'
      ? [
          { name: 'Succeeded', tone: 'ok', key: 'succeeded' },
          { name: 'Failed', tone: 'fail', key: 'failed' },
          { name: 'Retrying', tone: 'proc', key: 'retrying' },
          { name: 'Delayed', tone: 'sched', key: 'delayed' },
          { name: 'Scheduled', tone: 'muted', key: 'scheduled' },
          { name: 'Queued', tone: 'enq', key: 'queued' },
        ]
      : [
          { name: 'Succeeded', tone: 'ok', key: 'succeeded' },
          { name: 'Failed', tone: 'fail', key: 'failed' },
          { name: 'Retrying', tone: 'proc', key: 'retrying' },
        ]
  const parts = named.flatMap(({ name, tone, key }) => {
    const count = read(key)

    return count ? [{ name, tone, count }] : []
  })
  const exact = parts.length === named.length && parts.every((part) => !part.count.capped)

  if (kind === 'received') {
    const all = read('all')

    if (!exact || !all || all.capped) {
      return { parts, complete: false }
    }

    const waiting = all.value - parts.reduce((sum, part) => sum + part.count.value, 0)

    return {
      parts: [...parts, { name: 'Waiting', tone: 'enq', count: { value: Math.max(waiting, 0), capped: false } }],
      // A total below the sum of its parts means the counts were read at different moments: no share is exact then.
      complete: waiting >= 0,
    }
  }

  return { parts, complete: exact }
}

/** Every stored message by status, one donut per kind; a share shows only when every count behind it is exact. */
export function StatusDistributionCard() {
  const format = useFormat()
  const { query, counts, stale, fetchedAt } = useStatusCounts()

  return (
    <Card
      title="Messages by status"
      icon={ChartIcon}
      empty={
        query.isError ? (
          <LoadError message={query.error.message} onRetry={() => void query.refetch()} />
        ) : (
          <Skeleton label="Loading the messages by status" shape="chart" />
        )
      }
    >
      {counts
        ? (['received', 'published'] as const).map((kind) => {
            const { parts, complete } = statusParts(kind, counts)

            return (
              <CardRow key={kind}>
                <Text tone="hint">
                  {kind === 'received' ? 'All stored received messages' : 'All stored published messages'}
                </Text>
                <Chart
                  kind="donut"
                  label={kind === 'received' ? 'Received by status' : 'Published by status'}
                  unit="messages"
                  unitOne="message"
                  asOf={fetchedAt}
                  zone={format.timeZone}
                  complete={complete && !stale}
                  parts={parts}
                  empty={`No stored ${kind} messages`}
                />
              </CardRow>
            )
          })
        : null}
    </Card>
  )
}
