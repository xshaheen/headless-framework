import { Mono, PageFrame, PageHeader, Skeleton, usePageFrame } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { DataTable } from '@headless/dashboard-sdk/table'
import { AttentionGaps, CellText, LoadError, MessagingStats, Time, useMessagingAttention } from '../parts.tsx'

/** `[{node}:]unknown-lane:{store}:{kind}`; the store and kind are read from the end, as a node name may hold a colon. */
export function parseUnknownLane(id: string) {
  const marker = id.indexOf('unknown-lane:')

  if (marker < 0) {
    return undefined
  }

  const [store = '', kind = ''] = id.slice(marker + 'unknown-lane:'.length).split(':')

  return { node: marker > 0 ? id.slice(0, marker - 1) : '', store, kind }
}

/**
 * The rows whose lane no consumer registers, as the attention source reports them. The server serves no list of the
 * rows themselves, so the view shows each report and how many rows it counts.
 */
export default function UnknownLanesView() {
  const format = useFormat()
  const { answer, items, stale, unavailable, complete } = useMessagingAttention()
  const rows = items.flatMap((item) => {
    const lane = parseUnknownLane(item.id)

    return lane ? [{ ...item, ...lane }] : []
  })
  const frame = usePageFrame({ viewId: 'messaging.unknown-lanes', rows })

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title="Unknown lanes"
          tone={rows.length > 0 ? 'fail' : undefined}
          count={answer.data ? format.number(rows.length) : undefined}
          description="Stored rows on a lane no consumer registers; they wait until a host that consumes the lane runs."
        />
      }
    >
      {answer.isError ? (
        <LoadError message={answer.error.message} onRetry={() => void answer.refetch()} />
      ) : answer.isPending ? (
        <Skeleton label="Loading unknown lanes" rows={2} />
      ) : stale ? (
        <AttentionGaps stale unavailable={unavailable} onRetry={() => void answer.refetch()} />
      ) : (
        <>
          <AttentionGaps stale={false} unavailable={unavailable} onRetry={() => void answer.refetch()} />
          <DataTable
            label="Unknown lanes"
            viewId="messaging.unknown-lanes"
            frame={frame}
            rows={rows}
            // Only an answer from every source shows that no row sits on an unknown lane.
            emptyState={complete ? 'Every stored row is on a lane some consumer registers.' : 'No unknown lanes in the sources that answered.'}
            columns={[
              { id: 'title', header: 'Report', cell: (row) => <CellText title={row.title}>{row.title}</CellText>, isRowHeader: true, width: '2fr' },
              { id: 'store', header: 'Store', cell: (row) => <Mono>{row.store}</Mono>, width: 120 },
              { id: 'node', header: 'Node', cell: (row) => <Mono>{row.node}</Mono>, priority: 2, width: 120 },
              { id: 'count', header: 'Rows', cell: (row) => format.count({ value: row.count, capped: row.capped }), align: 'end', width: 90 },
              { id: 'seen', header: 'Last seen', cell: (row) => (row.lastSeenAt ? <Time value={row.lastSeenAt} short /> : ''), priority: 2, width: 110 },
            ]}
          />
        </>
      )}
    </PageFrame>
  )
}
