import {
  FilterToolbar,
  Mono,
  PageFrame,
  PageHeader,
  Tag,
  usePageFrame,
  QueryState,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { DataTable } from '@headless/dashboard-sdk/table'
import { RunsIcon } from '@headless/dashboard-sdk/icons'
import { CellText, MessagingStats, NodeLink, NodeSwitch, StaleNotice, useContracts } from '../parts.tsx'

export default function ContractsView() {
  const format = useFormat()
  const contracts = useContracts()
  const rows = (contracts.data ?? []).map((contract) => ({
    ...contract,
    id: `${contract.name}@${contract.contractVersion}`,
  }))
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
          toolbar={
            <FilterToolbar>
              <NodeSwitch />
            </FilterToolbar>
          }
        />
      }
    >
      <StaleNotice what="these contracts" onRefresh={() => void contracts.refetch()} failed={contracts.isError} />
      <QueryState query={contracts} label="Loading contracts" rows={4} shape="table">
        {() => (
          <DataTable
            label="Contracts"
            frame={frame}
            rows={rows}
            emptyIcon={RunsIcon}
            emptyTitle="No message contracts"
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
                kind: 'id',
                width: '2fr',
              },
              { id: 'version', header: 'Version', cell: (row) => row.contractVersion, kind: 'id', width: 90 },
              {
                id: 'lanes',
                header: 'Lanes',
                cell: (row) => (
                  <CellText title={row.lanes.map((lane) => lane.lane).join(', ')}>
                    {row.lanes.map((lane) => (
                      <Tag key={lane.lane} variant="square">
                        {[lane.lane, lane.deliveryMode, lane.rawPayload ? 'raw payload' : undefined]
                          .filter(Boolean)
                          .join(' · ')}
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
