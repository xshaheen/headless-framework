import { useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import { Button, DetailPanel, FieldGrid, GridField, Mono, PageFrame, PageHeader, StatusMark, usePageFrame } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { nodeMark, useMessagingCommands } from '../messages.ts'
import { CellText, QueryState, StaleNotice, useNode, type MessagingNode } from '../parts.tsx'

/** A node's row id is its name, which the server's node commands take as their id. */
type NodeRow = MessagingNode & { id: string }

export default function NodesView() {
  const format = useFormat()
  const { nodes, forwarding } = useNode()
  const rows: NodeRow[] = (nodes.data?.items ?? []).map((item) => ({ ...item, id: item.name }))
  const frame = usePageFrame({ viewId: 'messaging.nodes', rows })
  const open = rows.find((row) => row.id === frame.openId)
  const discovery = nodes.data?.discovery

  return (
    <PageFrame
      frame={frame}
      header={
        <PageHeader
          title="Nodes"
          count={nodes.data ? format.number(rows.length) : undefined}
          description={
            forwarding
              ? `This node and the nodes discovery lists; the last discovery read found ${discovery ?? 'nothing'}.`
              : 'Forwarding is off, so the dashboard reads and acts on this node alone.'
          }
        />
      }
      panel={open ? <NodePanel node={open} forwarding={forwarding} /> : undefined}
    >
      <StaleNotice what="these nodes" onRefresh={() => void nodes.refetch()} />
      <QueryState query={nodes} label="Loading nodes" rows={3}>
        {() => (
          <DataTable
            label="Nodes"
            viewId="messaging.nodes"
            frame={frame}
            rows={rows}
            emptyState="No node serves Messaging."
            columns={[
              {
                id: 'name',
                header: 'Node',
                cell: (row) => (
                  <CellText title={row.name}>
                    <Mono>{row.name}</Mono>
                  </CellText>
                ),
                isRowHeader: true,
                width: '2fr',
              },
              {
                id: 'state',
                header: 'State',
                cell: (row) => {
                  const mark = nodeMark(row)

                  return (
                    <CellText>
                      <StatusMark tone={mark.tone} label={mark.label} />
                    </CellText>
                  )
                },
                width: 150,
              },
              { id: 'stores', header: 'Stores', cell: (row) => <CellText title={row.stores.join(', ')}>{row.stores.join(', ')}</CellText>, priority: 2, width: '2fr' },
            ]}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

function NodePanel({ node, forwarding }: { node: NodeRow; forwarding: boolean }) {
  const module = useModule()
  const commands = useMessagingCommands()
  const [, setParams] = useSearchParams()
  const refusal = useAllowedActions('node', [node])
  const mark = nodeMark(node)
  const reset = commands.find('reset-backpressure')

  function readThisNode() {
    setParams((current) => {
      const next = new URLSearchParams(current)
      next.delete('open')

      if (node.isLocal) {
        next.delete('node')
      } else {
        next.set('node', node.name)
      }

      return next
    })
  }

  return (
    <DetailPanel
      label={`Node ${node.name}`}
      name={node.name}
      status={mark}
      actions={
        <>
          <Button variant="primary" disabledReason={node.isLocal || forwarding ? null : 'Forwarding is not configured'} onPress={readThisNode}>
            Read this node
          </Button>
          {reset ? (
            <CommandAction module={module.id} command={reset} ids={[node.id]} noun={`node ${node.name}`} commands={commands.all} disabledReason={refusal(reset.id)} />
          ) : null}
        </>
      }
    >
      <FieldGrid columns={2}>
        <GridField label="Serves">{node.isLocal ? 'This dashboard' : 'Forwarded calls'}</GridField>
        <GridField label="State">{mark.label}</GridField>
        <GridField label="Stores" span={2}>
          {node.stores.join(', ')}
        </GridField>
      </FieldGrid>
    </DetailPanel>
  )
}
