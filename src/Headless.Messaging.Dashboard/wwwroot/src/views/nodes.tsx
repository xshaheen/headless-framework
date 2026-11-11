import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import {
  Button,
  DetailPanel,
  FieldGrid,
  GridField,
  Mono,
  PageFrame,
  PageHeader,
  StatusMark,
  Tag,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { PingIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import type { DashboardStateRole } from '@headless/dashboard-sdk'
import { useState } from 'react'
import { nodeMark, useMessagingCommands, viewOnlyReason } from '../messages.ts'
import {
  CellText,
  QueryState,
  StaleNotice,
  useNode,
  type MessagingNode,
  type MessagingNodeInstance,
} from '../parts.tsx'

/** A node's row id is its name, which the server's node commands take as their id. */
type NodeRow = MessagingNode & { id: string }

/** One node's answer to a ping, as the server serves it. */
export interface PingAnswer {
  node: string
  outcome: 'answered' | 'timeout' | 'failed'
  latencyMs?: number | null
  state?: string | null
  status?: number | null
  reason?: string | null
}

/** A node's last ping on this page: waiting for the server, its answer, or why the server refused or failed the ping. */
type PingResult = { kind: 'pending' } | { kind: 'answer'; answer: PingAnswer } | { kind: 'refused'; reason: string }

/** The pings this page sent, by node, and the call that sends one node's or every node's. */
function usePing() {
  const { api } = useDashboard()
  const [results, setResults] = useState<ReadonlyMap<string, PingResult>>(new Map())

  async function ping(targets: readonly string[], one?: string) {
    setResults((current) => new Map([...current, ...targets.map((node) => [node, { kind: 'pending' }] as const)]))

    try {
      const answer = await api.request<{ items: PingAnswer[] }>({
        method: 'POST',
        path: 'messaging/nodes/ping',
        body: one ? { node: one } : {},
      })

      setResults((current) => {
        const next = new Map(current)

        // A node the answer leaves out was not pinged, such as one discovery dropped since the page read it.
        for (const node of targets) {
          next.delete(node)
        }

        for (const item of answer.items) {
          next.set(item.node, { kind: 'answer', answer: item })
        }

        return next
      })
    } catch (error) {
      // The refusal shows on each node it covers, in place: a viewer's `403`, or a node discovery no longer lists.
      const reason =
        error instanceof DashboardApiError && error.status === 403
          ? viewOnlyReason
          : error instanceof Error
            ? error.message
            : 'The ping failed'

      setResults(
        (current) => new Map([...current, ...targets.map((node) => [node, { kind: 'refused', reason }] as const)]),
      )
    }
  }

  return { results, ping }
}

/** A ping result as the row and the panel show it: a dot and a word, and the detail after it. */
export function pingMark(
  result: PingResult,
  duration: (ms: number) => string,
): { tone: DashboardStateRole; label: string; detail?: string } {
  if (result.kind === 'pending') {
    return { tone: 'proc', label: 'Pinging…' }
  }

  if (result.kind === 'refused') {
    return { tone: 'fail', label: 'Refused', detail: result.reason }
  }

  const { answer } = result

  switch (answer.outcome) {
    case 'answered':
      return {
        tone: 'ok',
        label: answer.latencyMs !== null && answer.latencyMs !== undefined ? duration(answer.latencyMs) : 'Answered',
      }
    case 'timeout':
      return { tone: 'fail', label: 'Timed out', detail: answer.reason ?? undefined }
    default:
      return {
        tone: 'fail',
        label: answer.state ? nodeMark({ isLocal: false, state: answer.state }).label : 'Failed',
        detail: [answer.status ? `HTTP ${answer.status}` : undefined, answer.reason].filter(Boolean).join(' · '),
      }
  }
}

/** An instance as discovery gave it: `10.0.4.7:8080`, or the address alone when it carries its port. */
function instanceText(instance: MessagingNodeInstance) {
  return instance.port > 0 ? `${instance.address}:${instance.port}` : instance.address
}

export default function NodesView() {
  const format = useFormat()
  const module = useModule()
  const { nodes, forwarding } = useNode()
  const rows: NodeRow[] = (nodes.data?.items ?? []).map((item) => ({ ...item, id: item.name }))
  const frame = usePageFrame({ viewId: 'messaging.nodes', rows })
  const open = rows.find((row) => row.id === frame.openId)
  const discovery = nodes.data?.discovery
  const { results, ping } = usePing()
  const peers = rows.filter((row) => !row.isLocal).map((row) => row.name)
  // Ping makes this node call others, so it needs operate permission, as a command does.
  const pingReason = !module.canOperate
    ? viewOnlyReason
    : !forwarding
      ? 'Forwarding is off, so there is no other node to ping'
      : peers.length === 0
        ? 'Discovery lists no other node'
        : null

  function pingOne(node: NodeRow) {
    void ping([node.name], node.name)
  }

  function rowPingReason(node: NodeRow) {
    return node.isLocal ? 'This node serves the page' : pingReason
  }

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
          actions={
            <Button icon={PingIcon} disabledReason={pingReason} onPress={() => void ping(peers)}>
              Ping all
            </Button>
          }
        />
      }
      panel={
        open ? (
          <NodePanel
            node={open}
            forwarding={forwarding}
            result={results.get(open.name)}
            pingReason={rowPingReason(open)}
            onPing={() => pingOne(open)}
          />
        ) : undefined
      }
    >
      <StaleNotice what="these nodes" onRefresh={() => void nodes.refetch()} />
      <QueryState query={nodes} label="Loading nodes" rows={3} shape="table">
        {() => (
          <DataTable
            label="Nodes"
            viewId="messaging.nodes"
            frame={frame}
            rows={rows}
            emptyState="No node serves Messaging."
            rowActions={(row) =>
              row.isLocal ? null : (
                <Button
                  icon={PingIcon}
                  aria-label={`Ping ${row.name}`}
                  tooltip="Ping"
                  disabledReason={rowPingReason(row)}
                  onPress={() => pingOne(row)}
                />
              )
            }
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
                kind: 'id',
                width: '1.5fr',
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
                kind: 'status',
                width: 150,
              },
              {
                id: 'ping',
                header: 'Ping',
                cell: (row) => {
                  const result = results.get(row.name)

                  if (!result) {
                    return ''
                  }

                  const mark = pingMark(result, format.duration)

                  return (
                    <CellText title={mark.detail ? `${mark.label}: ${mark.detail}` : mark.label}>
                      <StatusMark tone={mark.tone} label={mark.label} />
                    </CellText>
                  )
                },
                kind: 'status',
                width: 150,
              },
              {
                id: 'address',
                header: 'Address',
                cell: (row) => {
                  const text = (row.instances ?? []).map(instanceText).join(', ')

                  return (
                    <CellText title={text}>
                      <Mono>{text}</Mono>
                    </CellText>
                  )
                },
                kind: 'id',
                priority: 2,
                width: '1.5fr',
              },
              {
                id: 'tags',
                header: 'Tags',
                cell: (row) => {
                  const tags = [...new Set((row.instances ?? []).flatMap((instance) => instance.tags))]

                  return <CellText title={tags.join(', ')}>{tags.join(', ')}</CellText>
                },
                priority: 2,
                width: '1fr',
              },
              {
                id: 'stores',
                header: 'Stores',
                cell: (row) => <CellText title={row.stores.join(', ')}>{row.stores.join(', ')}</CellText>,
                priority: 2,
                width: '1fr',
              },
            ]}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

function NodePanel({
  node,
  forwarding,
  result,
  pingReason,
  onPing,
}: {
  node: NodeRow
  forwarding: boolean
  result: PingResult | undefined
  pingReason: string | null
  onPing: () => void
}) {
  const module = useModule()
  const format = useFormat()
  const commands = useMessagingCommands()
  const [, setParams] = useSearchParams()
  const refusal = useAllowedActions('node', [node])
  const mark = nodeMark(node)
  const reset = commands.find('reset-backpressure')
  const ping = result ? pingMark(result, format.duration) : undefined

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
          <Button
            variant="primary"
            disabledReason={node.isLocal || forwarding ? null : 'Forwarding is not configured'}
            onPress={readThisNode}
          >
            Read this node
          </Button>
          {node.isLocal ? null : (
            <Button icon={PingIcon} disabledReason={pingReason} onPress={onPing}>
              Ping
            </Button>
          )}
          {reset ? (
            <CommandAction
              module={module.id}
              command={reset}
              ids={[node.id]}
              noun={`node ${node.name}`}
              commands={commands.all}
              disabledReason={refusal(reset.id)}
            />
          ) : null}
        </>
      }
    >
      <div>
        <FieldGrid columns={2}>
          <GridField label="Serves">{node.isLocal ? 'This dashboard' : 'Forwarded calls'}</GridField>
          <GridField label="State">{mark.label}</GridField>
          {ping ? (
            <GridField label="Last ping" span={2}>
              {/* A polite region, so the answer is heard where the operator pressed Ping. */}
              <span role="status">{ping.detail ? `${ping.label}: ${ping.detail}` : ping.label}</span>
            </GridField>
          ) : null}
          <GridField label="Stores" span={2}>
            {node.stores.join(', ')}
          </GridField>
        </FieldGrid>
        {(node.instances ?? []).map((instance) => (
          <FieldGrid key={instanceText(instance)} label="Instance" columns={2}>
            <GridField label="Address">
              <Mono>{instance.address}</Mono>
            </GridField>
            <GridField label="Port">
              {instance.port > 0 ? format.number(instance.port, { useGrouping: false }) : 'In the address'}
            </GridField>
            <GridField label="Tags" span={2}>
              {instance.tags.length > 0
                ? instance.tags.map((tag) => (
                    <Tag key={tag} variant="square">
                      {tag}
                    </Tag>
                  ))
                : 'None'}
            </GridField>
          </FieldGrid>
        ))}
      </div>
    </DetailPanel>
  )
}
