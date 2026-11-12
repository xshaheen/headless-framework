import { useDashboard } from '@headless/dashboard-sdk'
import {
  Banner,
  Button,
  FilterSelect,
  FilterToolbar,
  Mono,
  PageFrame,
  PageHeader,
  StatusMark,
  usePageFrame,
  Timestamp,
  QueryState,
} from '@headless/dashboard-sdk/components'
import { FilterCombobox } from '@headless/dashboard-sdk/combobox'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { ScheduledIcon } from '@headless/dashboard-sdk/icons'
import { nodeQuery } from '../messages.ts'
import { CellText, MessagingStats, NodeSwitch, StaleNotice, useConsumers, useContracts, useNode } from '../parts.tsx'

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
  const name = params.get('name') ?? ''
  const contracts = useContracts()
  const consumers = useConsumers()
  const page = Math.max(Number.parseInt(params.get('page') ?? '1', 10) || 1, 1)
  const scheduled = useQuery({
    queryKey: ['messaging', 'scheduled', node, store, name, page],
    queryFn: () =>
      api.request<ScheduledList>({
        method: 'GET',
        path: 'messaging/scheduled',
        query: {
          ...(store ? { store } : {}),
          ...(name ? { name } : {}),
          page: String(page),
          perPage: String(perPage),
          ...nodeQuery(node),
        },
      }),
  })
  // A published row's id names the node that stores it, the local one included.
  const nodeName = node || local?.name || ''
  const rows = (scheduled.data?.items ?? []).map((item) => ({ ...item, id: item.storageId }))
  const frame = usePageFrame({ viewId: 'messaging.scheduled', rows })
  const served = nodes.data?.items.find((item) => (node ? item.name === node : item.isLocal)) ?? local
  const storeOptions = [...new Set([...(served?.stores ?? []), ...(store ? [store] : [])])].map((value) => ({
    value,
    label: value,
  }))
  const total = scheduled.data?.totalCount ?? 0
  const pages = Math.max(Math.ceil(total / perPage), 1)
  const nodeSuffix = node ? `&node=${encodeURIComponent(node)}` : ''
  // A host can declare hundreds of messages, so the name is typed to narrow rather than scanned in a select.
  const nameOptions = [
    ...new Set([
      ...(contracts.data ?? []).map((contract) => contract.name),
      ...(consumers.data ?? []).flatMap((consumer) => consumer.messages.map((message) => message.name)),
      ...(name ? [name] : []),
    ]),
  ]
    .sort()
    .map((value) => ({ value, label: value }))

  function setName(value: string) {
    update((next) => {
      next.delete('page')

      if (value) {
        next.set('name', value)
      } else {
        next.delete('name')
      }
    })
  }

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
          toolbar={
            <FilterToolbar active={(store ? 1 : 0) + (name ? 1 : 0)}>
              <NodeSwitch />
              <FilterSelect
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
              <FilterCombobox
                label="Message"
                emptyLabel="Any message"
                value={name}
                onChange={setName}
                options={nameOptions}
              />
            </FilterToolbar>
          }
        />
      }
    >
      <StaleNotice what="these deliveries" onRefresh={() => void scheduled.refetch()} failed={scheduled.isError} />
      <QueryState query={scheduled} label="Loading scheduled deliveries" rows={6} shape="table">
        {(data) =>
          data.supported ? (
            <>
              <DataTable
                label="Scheduled deliveries"
                frame={frame}
                rows={rows}
                emptyIcon={ScheduledIcon}
                emptyTitle="No scheduled deliveries"
                emptyState={
                  name
                    ? `No ${name} delivery is scheduled.`
                    : 'Nothing is scheduled. A delivery appears here when a host publishes a message with a delay.'
                }
                columns={[
                  {
                    id: 'name',
                    header: 'Message',
                    cell: (row) => (
                      <CellText title={row.messageName}>
                        <Link
                          to={`/messaging/published?open=${encodeURIComponent(`${nodeName}~${data.store}~${row.storageId}`)}${nodeSuffix}`}
                        >
                          <Mono>{row.messageName}</Mono>
                        </Link>
                      </CellText>
                    ),
                    isRowHeader: true,
                    kind: 'id',
                    width: '2fr',
                  },
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (row) => (
                      <CellText>
                        <StatusMark
                          tone={row.isLeased ? 'proc' : 'sched'}
                          label={row.isLeased ? 'Leased' : row.status}
                        />
                      </CellText>
                    ),
                    kind: 'status',
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
                    kind: 'id',
                    priority: 2,
                    width: '1fr',
                  },
                  {
                    id: 'attempts',
                    header: 'Attempts',
                    cell: (row) => format.number(row.inlineAttempts),
                    kind: 'number',
                    priority: 2,
                    width: 90,
                  },
                  {
                    id: 'due',
                    header: 'Due',
                    cell: (row) => (
                      <CellText>
                        <Timestamp value={row.expectedDueAt} scheduled />
                      </CellText>
                    ),
                    kind: 'time',
                    width: 110,
                  },
                ]}
              />
              {pages > 1 ? (
                <Banner as="nav" aria-label="Pages">
                  <Button
                    variant="ghost"
                    disabledReason={page > 1 ? null : 'This is the first page'}
                    onPress={() => goTo(page - 1)}
                  >
                    Previous
                  </Button>
                  <span>{`Page ${format.number(page)} of ${format.number(pages)}`}</span>
                  <Button
                    variant="ghost"
                    disabledReason={page < pages ? null : 'This is the last page'}
                    onPress={() => goTo(page + 1)}
                  >
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
