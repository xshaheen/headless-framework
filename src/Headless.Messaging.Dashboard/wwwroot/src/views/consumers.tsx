import { useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions } from '@headless/dashboard-sdk/commands'
import {
  DetailPanel,
  FieldGrid,
  FilterToolbar,
  GridField,
  Mono,
  PageFrame,
  PageHeader,
  Section,
  StatusMark,
  Tag,
  Text,
  usePageFrame,
  Timestamp,
  QueryState,
} from '@headless/dashboard-sdk/components'
import { FilterCombobox } from '@headless/dashboard-sdk/combobox'
import { ConsumerIcon, OpenCircuitIcon, RefreshIcon, type IconComponent } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { DataTable } from '@headless/dashboard-sdk/table'
import { useState } from 'react'
import {
  circuitLabels,
  circuitTones,
  formatSeconds,
  spanSeconds,
  useMessagingCommands,
  type MessageCommandName,
} from '../messages.ts'
import {
  CellText,
  MessagingStats,
  NodeLink,
  NodeSwitch,
  StaleNotice,
  useCircuits,
  useConsumers,
  useNode,
  type MessagingCircuit,
  type MessagingConsumer,
} from '../parts.tsx'

/**
 * The types that implement a consumer, short: the class name without its namespace, as the list has room for. A
 * consumer of several messages may name several.
 */
function implementations(consumer: MessagingConsumer) {
  return [...new Set(consumer.messages.flatMap((message) => (message.consumerType ? [message.consumerType] : [])))]
}

function shortType(type: string) {
  // A nested or generic type keeps its outer name: `Billing.OrderConsumer+Inner` reads `OrderConsumer+Inner`.
  const generic = type.indexOf('`')
  const plain = generic < 0 ? type : type.slice(0, generic)

  return plain.slice(plain.lastIndexOf('.') + 1)
}

export default function ConsumersView() {
  const format = useFormat()
  const { node, local } = useNode()
  const consumers = useConsumers()
  const circuits = useCircuits()
  // A host can register hundreds of consumers, so the message they consume narrows the list as the operator types.
  const [message, setMessage] = useState('')
  const messageOptions = [...new Set((consumers.data ?? []).flatMap((item) => item.messages.map((each) => each.name)))]
    .sort()
    .map((name) => ({ value: name, label: name }))
  const rows = (consumers.data ?? [])
    .filter((consumer) => !message || consumer.messages.some((each) => each.name === message))
    .map((consumer) => ({ ...consumer, id: consumer.identity }))
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
          toolbar={
            <FilterToolbar active={message ? 1 : 0}>
              <NodeSwitch />
              <FilterCombobox
                label="Message"
                emptyLabel="Any message"
                value={message}
                onChange={setMessage}
                options={messageOptions}
              />
            </FilterToolbar>
          }
        />
      }
      panel={
        open ? (
          <ConsumerPanel
            consumer={open}
            circuit={circuitRows.find((circuit) => circuit.consumer === open.identity)}
            nodeName={nodeName}
          />
        ) : undefined
      }
    >
      <StaleNotice what="these consumers" onRefresh={() => void consumers.refetch()} failed={consumers.isError} />
      <QueryState query={consumers} label="Loading consumers" rows={4} shape="table">
        {() => (
          <DataTable
            label="Consumers"
            frame={frame}
            rows={rows}
            emptyIcon={ConsumerIcon}
            emptyTitle="No consumers"
            emptyState={message ? `No consumer on this node consumes ${message}.` : 'This node registers no consumers.'}
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
                kind: 'id',
                width: '2fr',
              },
              {
                id: 'type',
                header: 'Implementation',
                cell: (row) => {
                  const types = implementations(row)

                  return (
                    <CellText title={types.join(', ')}>
                      <Mono>{types.map(shortType).join(', ')}</Mono>
                    </CellText>
                  )
                },
                kind: 'id',
                priority: 2,
                width: '1.5fr',
              },
              {
                id: 'messages',
                header: 'Messages',
                cell: (row) => (
                  <CellText title={row.messages.map((message) => message.name).join(', ')}>
                    {row.messages.map((message) => message.name).join(', ')}
                  </CellText>
                ),
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
                    <StatusMark
                      tone={row.runState === 'runs-here' ? 'ok' : 'off'}
                      label={row.runState === 'runs-here' ? 'Runs here' : 'Not here'}
                    />
                  </CellText>
                ),
                kind: 'status',
                width: 120,
              },
              {
                id: 'concurrency',
                header: 'Concurrency',
                cell: (row) => format.number(row.concurrency),
                kind: 'number',
                priority: 2,
                width: 110,
              },
            ]}
          />
        )}
      </QueryState>
      <Section
        label="Circuit breakers"
        caption={circuits.data ? format.number(circuits.data.length) : undefined}
        description="A breaker opens after repeated failures and closes again on its own; reset one to let its consumer run now."
      >
        <QueryState query={circuits} label="Loading circuit breakers" rows={2} shape="table">
          {(items) => <CircuitsTable circuits={items} nodeName={nodeName} />}
        </QueryState>
      </Section>
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
          kind: 'id',
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
          kind: 'status',
          width: 120,
        },
        {
          id: 'failures',
          header: 'Failures',
          cell: (row) => `${format.number(row.consecutiveFailures)} of ${format.number(row.failureThreshold)}`,
          kind: 'number',
          priority: 2,
          width: 100,
        },
        {
          id: 'opened',
          header: 'Opened',
          cell: (row) => (row.openedAt ? <Timestamp value={row.openedAt} /> : ''),
          kind: 'time',
          priority: 2,
          width: 110,
        },
        {
          id: 'remaining',
          header: 'Closes in',
          cell: (row) => {
            const seconds = row.estimatedRemainingOpenDuration
              ? spanSeconds(row.estimatedRemainingOpenDuration)
              : undefined

            return seconds === undefined ? '' : formatSeconds(Math.round(seconds), format.number)
          },
          kind: 'duration',
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

  const action = (name: MessageCommandName, icon: IconComponent, variant?: 'danger') => {
    const command = commands.find(name)

    return command ? (
      <CommandAction
        key={name}
        module={module.id}
        command={command}
        ids={[circuit.id]}
        noun={noun}
        variant={variant}
        icon={icon}
        appearance="icon"
        commands={commands.all}
        disabledReason={refusal(command.id)}
        // The breaker answers no stream topic, so its new state is read back at once rather than at the next poll.
        onDone={() => void client.invalidateQueries({ queryKey: ['messaging', 'circuits'] })}
      />
    ) : null
  }

  return (
    <>
      {action('reset-circuit', RefreshIcon)}
      {action('force-open', OpenCircuitIcon, 'danger')}
    </>
  )
}

function ConsumerPanel({
  consumer,
  circuit,
  nodeName,
}: {
  consumer: MessagingConsumer
  circuit?: MessagingCircuit
  nodeName: string
}) {
  const format = useFormat()
  const rate = consumer.rateLimit
  const breaker = consumer.circuitBreaker

  return (
    <DetailPanel
      label={`Consumer ${consumer.identity}`}
      name={consumer.identity}
      status={
        circuit
          ? { tone: circuitTones[circuit.state], label: `Circuit ${circuitLabels[circuit.state].toLowerCase()}` }
          : undefined
      }
      actions={circuit ? <CircuitActions circuit={circuit} nodeName={nodeName} /> : undefined}
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <>
              <div>
                <FieldGrid label="Delivery">
                  <GridField label="Lane">{consumer.lane}</GridField>
                  <GridField label="Delivery">{deliveryLabel(consumer)}</GridField>
                  <GridField label="Runs here">
                    {consumer.runState === 'runs-here' ? 'Yes' : 'No, registered only'}
                  </GridField>
                  <GridField label="Concurrency">{format.number(consumer.concurrency)}</GridField>
                  {consumer.consumeOnly ? (
                    <GridField label="Consume-only list">
                      {consumer.consumeOnly === 'listed' ? 'Listed' : 'Not listed'}
                    </GridField>
                  ) : null}
                  {consumer.inboxRetentionSeconds !== undefined ? (
                    <GridField label="Inbox retention">
                      {formatSeconds(consumer.inboxRetentionSeconds, format.number)}
                    </GridField>
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
                            breaker.failureThreshold !== undefined
                              ? `opens after ${format.number(breaker.failureThreshold)} failures`
                              : undefined,
                            breaker.openDurationSeconds !== undefined
                              ? `for ${formatSeconds(breaker.openDurationSeconds, format.number)}`
                              : undefined,
                          ]
                            .filter(Boolean)
                            .join(' ') || 'On'
                        : 'Off'}
                    </GridField>
                  ) : null}
                </FieldGrid>
                <FieldGrid label="Handlers" columns={2}>
                  {consumer.messages.map((message) => (
                    <GridField key={message.name} label={`${message.name} v${message.contractVersion}`} span={2}>
                      {message.consumerType ? (
                        <Mono>
                          {message.handlerMethod
                            ? `${message.consumerType}.${message.handlerMethod}`
                            : message.consumerType}
                        </Mono>
                      ) : (
                        'Not reported by this host'
                      )}
                    </GridField>
                  ))}
                </FieldGrid>
              </div>
              <section aria-label="Messages it consumes">
                <Text as="h3" tone="label">
                  Messages it consumes
                </Text>
                <div>
                  {consumer.messages.map((message) => (
                    <Tag key={message.name} variant="square">
                      <NodeLink
                        to={`received?name=${encodeURIComponent(message.name)}&consumer=${encodeURIComponent(consumer.identity)}`}
                      >
                        {`${message.name} v${message.contractVersion}`}
                      </NodeLink>
                    </Tag>
                  ))}
                </div>
              </section>
              {consumer.middleware && consumer.middleware.length > 0 ? (
                <section aria-label="Middleware">
                  <Text as="h3" tone="label">
                    Middleware
                  </Text>
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
