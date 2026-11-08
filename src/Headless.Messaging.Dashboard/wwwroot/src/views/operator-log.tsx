import { useDashboard } from '@headless/dashboard-sdk'
import { Mono, PageFrame, PageHeader, SelectField, StatusMark, usePageFrame } from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { useInfiniteQuery } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import { nodeQuery } from '../messages.ts'
import { CellText, LoadError, NodeSwitch, Skeleton, Time, useNode } from '../parts.tsx'

export interface OperatorReceipt {
  operationId: string
  targetKind: string
  operationType: string
  outcome: string
  actor: string
  reason?: string | null
  createdAt: string
  storageId?: string | null
  messageName?: string | null
  messageId?: string | null
  outboxName?: string | null
}

interface ReceiptPage {
  items: OperatorReceipt[]
  nextCursor?: string | null
}

const targetKinds = ['Inbox', 'ScheduledDelivery', 'Published'] as const
const targetLabels: Readonly<Record<string, string>> = { Inbox: 'Received', ScheduledDelivery: 'Scheduled delivery', Published: 'Published' }

function outcomeTone(outcome: string): DashboardStateRole {
  return outcome === 'Done' ? 'ok' : outcome === 'Failed' ? 'fail' : 'off'
}

/** The operator ledger in the main store, newest first: every audited intervention, a refused one included. */
export default function OperatorLogView() {
  const { api } = useDashboard()
  const { node } = useNode()
  const [params, setParams] = useSearchParams()
  const targetKind = params.get('targetKind') ?? ''
  const receipts = useInfiniteQuery({
    queryKey: ['messaging', 'receipts', node, targetKind],
    initialPageParam: '',
    queryFn: ({ pageParam }) =>
      api.request<ReceiptPage>({
        method: 'GET',
        path: 'messaging/receipts',
        query: { ...(targetKind ? { targetKind } : {}), ...(pageParam ? { after: pageParam } : {}), ...nodeQuery(node) },
      }),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })
  const rows = (receipts.data?.pages ?? []).flatMap((page) => page.items).map((receipt) => ({ ...receipt, id: receipt.operationId }))
  const frame = usePageFrame({ viewId: 'messaging.operator-log', rows })
  const noLedger = receipts.error instanceof DashboardApiError && receipts.error.status === 501

  return (
    <PageFrame
      frame={frame}
      header={
        <PageHeader title="Operator log" description="Every intervention an operator made on a stored message, newest first, a refused one included.">
          <NodeSwitch />
          <SelectField
            label="Target"
            emptyLabel="All targets"
            value={targetKind}
            onChange={(value) =>
              setParams(
                (current) => {
                  const next = new URLSearchParams(current)

                  if (value) {
                    next.set('targetKind', value)
                  } else {
                    next.delete('targetKind')
                  }

                  return next
                },
                { replace: true },
              )
            }
            options={targetKinds.map((value) => ({ value, label: targetLabels[value] ?? value }))}
          />
        </PageHeader>
      }
    >
      {noLedger ? (
        <div className="hd-banner" role="status">
          This store keeps no operator ledger, so no intervention is recorded.
        </div>
      ) : receipts.isError ? (
        <LoadError message={receipts.error.message} onRetry={() => void receipts.refetch()} />
      ) : receipts.isPending ? (
        <Skeleton label="Loading the operator log" rows={6} />
      ) : (
        <DataTable
          label="Operator log"
          viewId="messaging.operator-log"
          frame={frame}
          rows={rows}
          emptyState="No operator has acted on a stored message yet."
          hasMore={receipts.hasNextPage}
          isLoadingMore={receipts.isFetchingNextPage}
          onLoadMore={() => void receipts.fetchNextPage()}
          columns={[
            {
              id: 'operation',
              header: 'Action',
              cell: (row) => (
                <CellText title={row.reason ?? undefined}>{`${row.operationType} · ${targetLabels[row.targetKind] ?? row.targetKind}`}</CellText>
              ),
              isRowHeader: true,
              width: '1fr',
            },
            {
              id: 'outcome',
              header: 'Outcome',
              cell: (row) => (
                <CellText>
                  <StatusMark tone={outcomeTone(row.outcome)} label={row.outcome} />
                </CellText>
              ),
              width: 110,
            },
            {
              id: 'message',
              header: 'Message',
              cell: (row) => (
                <CellText title={row.messageId ?? undefined}>
                  <Mono>{[row.messageName, row.outboxName ? `(${row.outboxName})` : undefined].filter(Boolean).join(' ')}</Mono>
                </CellText>
              ),
              priority: 2,
              width: '2fr',
            },
            { id: 'actor', header: 'Operator', cell: (row) => <CellText title={row.actor}>{row.actor}</CellText>, width: '1fr' },
            { id: 'reason', header: 'Reason', cell: (row) => <CellText title={row.reason ?? undefined}>{row.reason ?? ''}</CellText>, priority: 2, width: '2fr' },
            { id: 'at', header: 'At', cell: (row) => <Time value={row.createdAt} short />, width: 110 },
          ]}
        />
      )}
    </PageFrame>
  )
}
