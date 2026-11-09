import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import {
  DetailPanel,
  FieldGrid,
  GridField,
  Mono,
  PageFrame,
  PageHeader,
  StatusMark,
  Tag,
  Text,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { DataTable } from '@headless/dashboard-sdk/table'
import { circuitTones, formatSeconds, nodeQuery, useMessagingCommands, type MessageCommandName } from '../messages.ts'
import {
  CellText,
  MessagingStats,
  NodeLink,
  NodeSwitch,
  QueryState,
  StaleNotice,
  Time,
  useConsumers,
  useNode,
  type MessagingCircuit,
  type MessagingConsumer,
} from '../parts.tsx'

const circuitLabels: Readonly<Record<MessagingCircuit['state'], string>> = { Closed: 'Closed', Open: 'Open', HalfOpen: 'Half open' }

/** A consumer's circuits on the node the views read; the breaker keeps its state in memory, so it is polled. */
function useCircuits() {
  const { api } = useDashboard()
  const { node } = useNode()

  return useQuery({
    queryKey: ['messaging', 'circuits', node],
    queryFn: async () => (await api.request<{ items: MessagingCircuit[] }>({ method: 'GET', path: 'messaging/circuits', query: nodeQuery(node) })).items,
    refetchInterval: 5_000,
  })
}

/** A .NET time span, `[d.]hh:mm:ss[.fffffff]`, in seconds; `undefined` when it is not one. */
export function spanSeconds(span: string) {
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(span)

  if (!match) {
    return undefined
  }

  const [, days = '0', hours = '0', minutes = '0', seconds = '0'] = match

  return Number(days) * 86_400 + Number(hours) * 3_600 + Number(minutes) * 60 + Number(seconds)
}

export default function ConsumersView() {
  const format = useFormat()
  const { node, local } = useNode()
  const consumers = useConsumers()
  const circuits = useCircuits()
  const rows = (consumers.data ?? []).map((consumer) => ({ ...consumer, id: consumer.identity }))
  const frame = usePageFrame({ viewId: 'messaging.consumers', rows })
  const nodeName = node || local?.name || ''
  // A circuit's link opens its consumer, as the circuit is one consumer's breaker on one node.
  const openCircuit = circuits.data?.find((circuit) => circuit.id === frame.openId)
  const open = rows.find((row) => row.id === (openCircuit?.consumer ?? frame.openId))
  const circuitRows = circuits.data ?? []

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title="Consumers"
          count={consumers.data ? format.number(rows.length) : undefined}
          description="Every consumer the node registers, with how it runs and its circuit breaker."
        >
          <NodeSwitch />
        </PageHeader>
      }
      panel={open ? <ConsumerPanel consumer={open} circuit={circuitRows.find((circuit) => circuit.consumer === open.identity)} nodeName={nodeName} /> : undefined}
    >
      <StaleNotice what="these consumers" onRefresh={() => void consumers.refetch()} />
      <QueryState query={consumers} label="Loading consumers" rows={4}>
        {() => (
          <DataTable
            label="Consumers"
            viewId="messaging.consumers"
            frame={frame}
            rows={rows}
            emptyState="This node registers no consumers."
            columns={[
              {
                id: 'identity',
                header: 'Consumer',
                cell: (row) => (
                  <CellText title={row.identity}>
                    <Mono>{row.identity}</Mono>
                  </CellText>
                ),
                isRowHeader: true,
                width: '2fr',
              },
              {
                id: 'messages',
                header: 'Messages',
                cell: (row) => <CellText title={row.messages.map((message) => message.name).join(', ')}>{row.messages.map((message) => message.name).join(', ')}</CellText>,
                priority: 2,
                width: '2fr',
              },
              { id: 'lane', header: 'Lane', cell: (row) => row.lane, priority: 2, width: 100 },
              { id: 'delivery', header: 'Delivery', cell: (row) => deliveryLabel(row), priority: 2, width: 130 },
              {
                id: 'runs',
                header: 'Runs here',
                cell: (row) => (
                  <CellText>
                    <StatusMark tone={row.runState === 'runs-here' ? 'ok' : 'off'} label={row.runState === 'runs-here' ? 'Runs here' : 'Not here'} />
                  </CellText>
                ),
                width: 120,
              },
              { id: 'concurrency', header: 'Concurrency', cell: (row) => format.number(row.concurrency), align: 'end', priority: 2, width: 110 },
            ]}
          />
        )}
      </QueryState>
      <section aria-label="Circuit breakers">
        <Text as="h2" tone="title">Circuit breakers</Text>
        <QueryState query={circuits} label="Loading circuit breakers" rows={2}>
          {(items) => <CircuitsTable circuits={items} nodeName={nodeName} />}
        </QueryState>
      </section>
    </PageFrame>
  )
}

function deliveryLabel(consumer: Pick<MessagingConsumer, 'delivery'>) {
  return consumer.delivery === 'every-instance' ? 'Every instance' : 'Competing'
}

function CircuitsTable({ circuits, nodeName }: { circuits: readonly MessagingCircuit[]; nodeName: string }) {
  const format = useFormat()

  return (
    <DataTable
      label="Circuit breakers"
      viewId="messaging.circuits"
      rows={circuits}
      emptyState="No consumer has a circuit breaker that has seen a failure on this node."
      rowActions={(circuit) => <CircuitActions circuit={circuit} nodeName={nodeName} />}
      columns={[
        {
          id: 'consumer',
          header: 'Consumer',
          cell: (row) => (
            <CellText title={row.consumer}>
              <NodeLink to={`consumers?open=${encodeURIComponent(row.consumer)}`}>
                <Mono>{row.consumer}</Mono>
              </NodeLink>
            </CellText>
          ),
          isRowHeader: true,
          width: '2fr',
        },
        {
          id: 'state',
          header: 'State',
          cell: (row) => (
            <CellText>
              <StatusMark tone={circuitTones[row.state]} label={circuitLabels[row.state]} />
            </CellText>
          ),
          width: 120,
        },
        {
          id: 'failures',
          header: 'Failures',
          cell: (row) => `${format.number(row.consecutiveFailures)} of ${format.number(row.failureThreshold)}`,
          align: 'end',
          priority: 2,
          width: 100,
        },
        { id: 'opened', header: 'Opened', cell: (row) => (row.openedAt ? <Time value={row.openedAt} short /> : ''), priority: 2, width: 110 },
        {
          id: 'remaining',
          header: 'Closes in',
          cell: (row) => {
            const seconds = row.estimatedRemainingOpenDuration ? spanSeconds(row.estimatedRemainingOpenDuration) : undefined

            return seconds === undefined ? '' : formatSeconds(Math.round(seconds), format.number)
          },
          align: 'end',
          priority: 2,
          width: 100,
        },
      ]}
    />
  )
}

/**
 * Reset and Force open, as the server allows them now. Both act on the node that serves the circuit, so the toast
 * names it; forcing a circuit open stops the consumer, so it asks first, and the command's definition says so.
 */
function CircuitActions({ circuit, nodeName }: { circuit: MessagingCircuit; nodeName: string }) {
  const module = useModule()
  const client = useQueryClient()
  const commands = useMessagingCommands()
  const refusal = useAllowedActions('circuit', [circuit])
  const noun = `circuit of ${circuit.consumer}${nodeName ? ` on ${nodeName}` : ''}`

  const action = (name: MessageCommandName, variant?: 'danger') => {
    const command = commands.find(name)

    return command ? (
      <CommandAction
        key={name}
        module={module.id}
        command={command}
        ids={[circuit.id]}
        noun={noun}
        variant={variant}
        commands={commands.all}
        disabledReason={refusal(command.id)}
        // The breaker answers no stream topic, so its new state is read back at once rather than at the next poll.
        onDone={() => void client.invalidateQueries({ queryKey: ['messaging', 'circuits'] })}
      />
    ) : null
  }

  return (
    <>
      {action('reset-circuit')}
      {action('force-open', 'danger')}
    </>
  )
}

function ConsumerPanel({ consumer, circuit, nodeName }: { consumer: MessagingConsumer; circuit?: MessagingCircuit; nodeName: string }) {
  const format = useFormat()
  const rate = consumer.rateLimit
  const breaker = consumer.circuitBreaker

  return (
    <DetailPanel
      label={`Consumer ${consumer.identity}`}
      name={consumer.identity}
      status={circuit ? { tone: circuitTones[circuit.state], label: `Circuit ${circuitLabels[circuit.state].toLowerCase()}` } : undefined}
      actions={circuit ? <CircuitActions circuit={circuit} nodeName={nodeName} /> : undefined}
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <>
              <div>
                <FieldGrid columns={2}>
                  <GridField label="Lane">{consumer.lane}</GridField>
                  <GridField label="Delivery">{deliveryLabel(consumer)}</GridField>
                  <GridField label="Runs here">{consumer.runState === 'runs-here' ? 'Yes' : 'No, registered only'}</GridField>
                  <GridField label="Concurrency">{format.number(consumer.concurrency)}</GridField>
                  {consumer.consumeOnly ? <GridField label="Consume-only list">{consumer.consumeOnly === 'listed' ? 'Listed' : 'Not listed'}</GridField> : null}
                  {consumer.inboxRetentionSeconds !== undefined ? (
                    <GridField label="Inbox retention">{formatSeconds(consumer.inboxRetentionSeconds, format.number)}</GridField>
                  ) : null}
                  {rate ? (
                    <GridField label="Rate limit" span={2}>
                      {rate.algorithm === 'token-bucket'
                        ? `${format.number(rate.tokensPerPeriod)} per ${formatSeconds(rate.replenishmentPeriodSeconds, format.number)}, bursts of ${format.number(rate.tokenLimit)}`
                        : `${format.number(rate.permitLimit)} per ${formatSeconds(rate.windowSeconds, format.number)}`}
                    </GridField>
                  ) : null}
                  {breaker ? (
                    <GridField label="Circuit breaker" span={2}>
                      {breaker.enabled
                        ? [
                            breaker.failureThreshold !== undefined ? `opens after ${format.number(breaker.failureThreshold)} failures` : undefined,
                            breaker.openDurationSeconds !== undefined ? `for ${formatSeconds(breaker.openDurationSeconds, format.number)}` : undefined,
                          ]
                            .filter(Boolean)
                            .join(' ') || 'On'
                        : 'Off'}
                    </GridField>
                  ) : null}
                </FieldGrid>
              </div>
              <section aria-label="Messages it consumes">
                <Text as="h3" tone="label">Messages it consumes</Text>
                <div>
                  {consumer.messages.map((message) => (
                    <Tag key={message.name} variant="square">
                      <NodeLink to={`received?name=${encodeURIComponent(message.name)}&consumer=${encodeURIComponent(consumer.identity)}`}>
                        {`${message.name} v${message.contractVersion}`}
                      </NodeLink>
                    </Tag>
                  ))}
                </div>
              </section>
              {consumer.middleware && consumer.middleware.length > 0 ? (
                <section aria-label="Middleware">
                  <Text as="h3" tone="label">Middleware</Text>
                  <div>
                    {consumer.middleware.map((name) => (
                      <Tag key={name} variant="square">
                        {name}
                      </Tag>
                    ))}
                  </div>
                </section>
              ) : null}
            </>
          ),
        },
      ]}
    />
  )
}
