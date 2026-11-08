import { Mono, PageFrame, PageHeader, Tag, usePageFrame } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { DataTable } from '@headless/dashboard-sdk/table'
import { CellText, MessagingStats, NodeLink, NodeSwitch, QueryState, StaleNotice, useContracts } from '../parts.tsx'

export default function ContractsView() {
  const format = useFormat()
  const contracts = useContracts()
  const rows = (contracts.data ?? []).map((contract) => ({ ...contract, id: `${contract.name}@${contract.contractVersion}` }))
  const frame = usePageFrame({ viewId: 'messaging.contracts', rows })

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title="Contracts"
          count={contracts.data ? format.number(rows.length) : undefined}
          description="The message contracts the node registers, with the lanes each one travels on."
        >
          <NodeSwitch />
        </PageHeader>
      }
    >
      <StaleNotice what="these contracts" onRefresh={() => void contracts.refetch()} />
      <QueryState query={contracts} label="Loading contracts" rows={4}>
        {() => (
          <DataTable
            label="Contracts"
            viewId="messaging.contracts"
            frame={frame}
            rows={rows}
            emptyState="This node registers no message contracts."
            columns={[
              {
                id: 'name',
                header: 'Message',
                cell: (row) => (
                  <CellText title={row.name}>
                    <NodeLink to={`received?name=${encodeURIComponent(row.name)}`}>
                      <Mono>{row.name}</Mono>
                    </NodeLink>
                  </CellText>
                ),
                isRowHeader: true,
                width: '2fr',
              },
              { id: 'version', header: 'Version', cell: (row) => <Mono>{row.contractVersion}</Mono>, width: 90 },
              {
                id: 'lanes',
                header: 'Lanes',
                cell: (row) => (
                  <CellText title={row.lanes.map((lane) => lane.lane).join(', ')}>
                    {row.lanes.map((lane) => (
                      <Tag key={lane.lane} variant="square">
                        {[lane.lane, lane.deliveryMode, lane.rawPayload ? 'raw payload' : undefined].filter(Boolean).join(' · ')}
                      </Tag>
                    ))}
                  </CellText>
                ),
                priority: 2,
                width: '2fr',
              },
            ]}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}
