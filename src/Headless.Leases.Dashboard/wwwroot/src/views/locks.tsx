import { useDashboard } from '@headless/dashboard-sdk'
import {
  Banner,
  DetailPanel,
  EmptyState,
  FieldGrid,
  GridField,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  StatCard,
  StatStrip,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList } from '@headless/dashboard-sdk/data'
import { ListIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { DataTable } from '@headless/dashboard-sdk/table'
import {
  fetchLocks,
  locksTopic,
  pageReadAt,
  pageTotal,
  queryKeys,
  useLock,
  useSummary,
  type HeldLock,
  type HeldLockPage,
} from '../api.ts'
import { CellText, isNotFound, LeaseTypeSwitch, ReadAt } from '../parts.tsx'

/** No abstraction says where a provider looks, so the note names every provider's scope instead of guessing one. */
const scopeNote =
  'Redis lists every holder across the cluster. PostgreSQL, SQL Server, and in-memory providers list only the locks ' +
  "this host's handles hold, so behind a load balancer this list is the answering replica's. Fencing tokens are " +
  "reported only on the holder's own handle, so none shows here."

export default function DistributedLocksView() {
  const { api } = useDashboard()
  const format = useFormat()
  const client = useQueryClient()
  const summary = useSummary()
  const hasLocks = summary.data?.locks != null
  const locks = useDashboardKeysetList<HeldLock>({
    topic: locksTopic,
    params: { enabled: hasLocks },
    fetchPage: (cursor): Promise<HeldLockPage> =>
      hasLocks ? fetchLocks(api, cursor) : Promise.resolve({ items: [], nextCursor: null, total: 0, readAt: '' }),
  })
  const frame = usePageFrame({ viewId: 'leases.locks', rows: locks.rows })
  const openId = frame.openId
  const firstPage = locks.query.data?.pages[0]
  const total = hasLocks ? pageTotal(firstPage) : undefined

  function refresh() {
    void locks.query.refetch()

    if (openId) {
      void client.invalidateQueries({ queryKey: queryKeys.lock(openId) })
    }
  }

  if (summary.data && !summary.data.locks) {
    return (
      <PageFrame frame={frame} header={<PageHeader title="Distributed locks" />}>
        <EmptyState icon={ListIcon} title="No distributed lock provider is registered" placement="page">
          This host serves no distributed locks. Call services.AddHeadlessDistributedLocks(...) with a provider to show
          the locks it holds here.
        </EmptyState>
      </PageFrame>
    )
  }

  return (
    <PageFrame
      frame={frame}
      switcher={
        summary.data?.fencing && summary.data?.locks ? (
          <LeaseTypeSwitch current="locks" summary={summary.data} />
        ) : undefined
      }
      stats={
        <StatStrip label="Locks at a glance">
          <StatCard label="Held locks" value={total === undefined ? '–' : format.number(total)} />
        </StatStrip>
      }
      header={
        <PageHeader
          title="Distributed locks"
          count={total === undefined ? undefined : `${format.number(total)} held`}
          description="Read-only. Time to live is as the provider read it; it does not count down."
          actions={<ReadAt at={pageReadAt(firstPage)} isRefreshing={locks.query.isFetching} onRefresh={refresh} />}
        />
      }
      panel={openId ? <LockPanel resource={openId} /> : undefined}
    >
      {hasLocks ? <Banner>{scopeNote}</Banner> : null}
      <QueryState query={summary} label="Loading locks" rows={4} shape="table">
        {() => (
          <QueryState query={locks.query} label="Loading locks" rows={4} shape="table">
            {() => (
              <DataTable
                label="Distributed locks"
                frame={frame}
                rows={locks.rows}
                hasMore={locks.hasMore}
                isLoadingMore={locks.isLoadingMore}
                onLoadMore={locks.loadMore}
                emptyIcon={ListIcon}
                emptyTitle="No locks are held"
                emptyState="A lock appears here while code holds it through IDistributedLock. Refresh to read again."
                columns={[
                  {
                    id: 'resource',
                    header: 'resource',
                    cell: (row) => (
                      <CellText title={row.resource}>
                        <Mono>{row.resource}</Mono>
                      </CellText>
                    ),
                    isRowHeader: true,
                    kind: 'id',
                    width: '2fr',
                  },
                  {
                    id: 'leaseId',
                    header: 'lease id',
                    cell: (row) => <CellText title={row.leaseId ?? undefined}>{leaseIdText(row)}</CellText>,
                    kind: 'id',
                    // Fixed to a whole 32-character id, since ids share a prefix and a cut one reads like the next.
                    // Below 768 px it drops, leaving the name its room, and the open lock's panel shows it whole.
                    priority: 2,
                    width: 260,
                  },
                  {
                    id: 'ttl',
                    header: 'time to live',
                    cell: (row) => <CellText>{ttlText(row, format.duration)}</CellText>,
                    kind: 'duration',
                    width: 140,
                  },
                ]}
              />
            )}
          </QueryState>
        )}
      </QueryState>
    </PageFrame>
  )
}

function leaseIdText(lock: HeldLock) {
  return lock.leaseId ? <Mono>{lock.leaseId}</Mono> : '—'
}

function ttlText(lock: HeldLock, duration: (ms: number) => string) {
  return lock.ttlMilliseconds == null ? 'No expiry' : duration(lock.ttlMilliseconds)
}

function LockPanel({ resource }: { resource: string }) {
  const format = useFormat()
  const detail = useLock(resource)

  if (isNotFound(detail.error)) {
    return (
      <DetailPanel label={`Lock ${resource}`} name={resource}>
        <Banner tone="warn">This lock is no longer held, so the provider no longer lists it.</Banner>
      </DetailPanel>
    )
  }

  return (
    <DetailPanel label={`Lock ${resource}`} name={resource}>
      <QueryState query={detail} label="Loading the lock" rows={3} shape="fields">
        {({ lock }) => (
          <FieldGrid
            label="Lock"
            footnote="Providers report a fencing token only on the holder's own handle, so none shows here."
          >
            <GridField label="Resource" span={2}>
              <Mono>{lock.resource}</Mono>
            </GridField>
            <GridField label="Lease id" span={2}>
              {leaseIdText(lock)}
            </GridField>
            <GridField label="Time to live, at read time">{ttlText(lock, format.duration)}</GridField>
          </FieldGrid>
        )}
      </QueryState>
    </DetailPanel>
  )
}
