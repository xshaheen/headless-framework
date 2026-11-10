import { useDashboard } from '@headless/dashboard-sdk'
import { Banner, Button, Mono, PageFrame, PageHeader, SelectField, StatusMark, usePageFrame } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { nodeQuery } from '../messages.ts'
import { CellText, MessagingStats, NodeSwitch, QueryState, StaleNotice, Time, useNode } from '../parts.tsx'

export interface ScheduledDelivery {
  storageId: string
  messageId: string
  messageName: string
  lane: string
  expectedDueAt: string
  status: string
  isLeased: boolean
  owner?: string | null
  lockedUntil?: string | null
  inlineAttempts: number
}

export interface ScheduledList {
  store: string
  supported: boolean
  items: ScheduledDelivery[]
  totalCount: number
  /** Why the store cannot list its scheduled deliveries, when it cannot. */
  reason?: string | null
}

const perPage = 20

export default function ScheduledView() {
  const { api } = useDashboard()
  const format = useFormat()
  const [params, setParams] = useSearchParams()
  const { node, nodes, local } = useNode()
  const store = params.get('store') ?? ''
  const page = Math.max(Number.parseInt(params.get('page') ?? '1', 10) || 1, 1)
  const scheduled = useQuery({
    queryKey: ['messaging', 'scheduled', node, store, page],
    queryFn: () =>
      api.request<ScheduledList>({
        method: 'GET',
        path: 'messaging/scheduled',
        query: { ...(store ? { store } : {}), page: String(page), perPage: String(perPage), ...nodeQuery(node) },
      }),
  })
  // A published row's id names the node that stores it, the local one included.
  const nodeName = node || local?.name || ''
  const rows = (scheduled.data?.items ?? []).map((item) => ({ ...item, id: item.storageId }))
  const frame = usePageFrame({ viewId: 'messaging.scheduled', rows })
  const served = nodes.data?.items.find((item) => (node ? item.name === node : item.isLocal)) ?? local
  const storeOptions = [...new Set([...(served?.stores ?? []), ...(store ? [store] : [])])].map((value) => ({ value, label: value }))
  const total = scheduled.data?.totalCount ?? 0
  const pages = Math.max(Math.ceil(total / perPage), 1)
  const nodeSuffix = node ? `&node=${encodeURIComponent(node)}` : ''

  function update(change: (next: URLSearchParams) => void) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        change(next)

        return next
      },
      { replace: true },
    )
  }

  function goTo(target: number) {
    update((next) => (target > 1 ? next.set('page', String(target)) : next.delete('page')))
  }

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title="Scheduled deliveries"
          count={scheduled.data?.supported ? format.number(total) : undefined}
          description="Messages the store holds for a later delivery, soonest first."
        >
          <NodeSwitch />
          <SelectField
            label="Store"
            emptyLabel="Main store"
            value={store}
            onChange={(value) =>
              update((next) => {
                next.delete('page')

                if (value) {
                  next.set('store', value)
                } else {
                  next.delete('store')
                }
              })
            }
            options={storeOptions}
          />
        </PageHeader>
      }
    >
      <StaleNotice what="these deliveries" onRefresh={() => void scheduled.refetch()} />
      <QueryState query={scheduled} label="Loading scheduled deliveries" rows={6}>
        {(data) =>
          data.supported ? (
            <>
              <DataTable
                label="Scheduled deliveries"
                viewId="messaging.scheduled"
                frame={frame}
                rows={rows}
                emptyState="Nothing is scheduled. A delivery appears here when a host publishes a message with a delay."
                columns={[
                  {
                    id: 'name',
                    header: 'Message',
                    cell: (row) => (
                      <CellText title={row.messageName}>
                        <Link to={`/messaging/published?open=${encodeURIComponent(`${nodeName}~${data.store}~${row.storageId}`)}${nodeSuffix}`}>
                          <Mono>{row.messageName}</Mono>
                        </Link>
                      </CellText>
                    ),
                    isRowHeader: true,
                    width: '2fr',
                  },
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (row) => (
                      <CellText>
                        <StatusMark tone={row.isLeased ? 'proc' : 'sched'} label={row.isLeased ? 'Leased' : row.status} />
                      </CellText>
                    ),
                    width: 130,
                  },
                  { id: 'lane', header: 'Lane', cell: (row) => row.lane, priority: 2, width: 100 },
                  {
                    id: 'owner',
                    header: 'Leased by',
                    cell: (row) => (
                      <CellText title={row.owner ?? undefined}>
                        <Mono>{row.owner ?? ''}</Mono>
                      </CellText>
                    ),
                    priority: 2,
                    width: '1fr',
                  },
                  { id: 'attempts', header: 'Attempts', cell: (row) => format.number(row.inlineAttempts), align: 'end', priority: 2, width: 90 },
                  {
                    id: 'due',
                    header: 'Due',
                    cell: (row) => (
                      <CellText>
                        <Time value={row.expectedDueAt} short />
                      </CellText>
                    ),
                    width: 110,
                  },
                ]}
              />
              {pages > 1 ? (
                <Banner as="nav" aria-label="Pages">
                  <Button variant="ghost" disabledReason={page > 1 ? null : 'This is the first page'} onPress={() => goTo(page - 1)}>
                    Previous
                  </Button>
                  <span>{`Page ${format.number(page)} of ${format.number(pages)}`}</span>
                  <Button variant="ghost" disabledReason={page < pages ? null : 'This is the last page'} onPress={() => goTo(page + 1)}>
                    Next
                  </Button>
                </Banner>
              ) : null}
            </>
          ) : (
            <Banner role="status">
              {`The ${data.store} store cannot list its scheduled deliveries${data.reason ? `: ${data.reason}` : '.'}`}
            </Banner>
          )
        }
      </QueryState>
    </PageFrame>
  )
}
