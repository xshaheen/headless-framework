import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions, useOperations, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import {
  Banner,
  Button,
  DetailPanel,
  FilterChip,
  FilterChips,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  SelectField,
  Skeleton,
  StatusMark,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardKeysetList, useDashboardObject, useNewRows } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable, type DataTableColumn } from '@headless/dashboard-sdk/table'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  CellText,
  MessagePanel,
  MessagingStats,
  NodeSwitch,
  QueryState,
  StaleNotice,
  Time,
  offers,
  rowCommands,
  useConsumers,
  useContracts,
  useMessagingAttention,
  useNode,
  useNodeReset,
  useNodes,
} from '../parts.tsx'
import {
  canScope,
  fetchMessage,
  fetchMessages,
  filterNames,
  listName,
  markDone,
  messageMatches,
  messagesNoun,
  operationScope,
  parseFailureGroup,
  readFilter,
  rowMark,
  topicOf,
  useMessagingCommands,
  usePending,
  versionsOf,
  viewOnlyReason,
  type MessageCommandName,
  type MessageFilter,
  type MessageKind,
  type MessageRow,
  type Pending,
} from '../messages.ts'

const titles: Readonly<Record<MessageKind, string>> = { published: 'published', received: 'received' }

/** Each list's title and what it holds, by the sidebar list the filter is. */
function describe(kind: MessageKind, list: string): { title: string; description: string } {
  const noun = titles[kind]

  switch (list) {
    case 'failed':
      return {
        title: 'Failed',
        description:
          kind === 'received'
            ? 'Failed on their last retry; re-execute them once the cause is fixed.'
            : 'Failed to reach the broker on their last retry; re-send them once the cause is fixed.',
      }
    case 'retrying':
      return { title: 'Retrying', description: 'Failed, with a retry pending.' }
    case 'held':
      return { title: 'Held', description: 'Kept from their consumer until an operator releases them.' }
    case 'succeeded':
      return { title: 'Succeeded', description: kind === 'received' ? 'Consumed without an exception.' : 'Handed to the broker.' }
    case 'scheduled':
      return { title: 'Scheduled', description: 'Waiting in the store for their due time.' }
    case 'delayed':
      return { title: 'Delayed', description: 'Published with a delay that has not passed yet.' }
    case 'queued':
      return { title: 'Queued', description: 'Due now, waiting for a node to pick them up.' }
    default:
      return { title: `All ${noun}`, description: `Every ${noun} message the store keeps, newest first.` }
  }
}

/** The commands an Operation can run over every row a filter matches, as the server registers them. */
const operationCommands: Readonly<Record<MessageKind, readonly { name: MessageCommandName; variant?: 'primary' | 'danger' }[]>> = {
  received: [{ name: 're-execute', variant: 'primary' }, { name: 'purge', variant: 'danger' }],
  published: [{ name: 're-send', variant: 'primary' }, { name: 'delete', variant: 'danger' }],
}

export function MessagesView({ kind }: { kind: MessageKind }) {
  const { api } = useDashboard()
  const format = useFormat()
  const [params, setParams] = useSearchParams()
  const { node } = useNode()
  const filter = readFilter(params, kind)
  const filterKey = [node, ...filterNames.map((name) => filter[name])].join('\n')
  const topic = topicOf(kind, node)
  const listParams = { ...filter, node }
  const messages = useDashboardKeysetList<MessageRow>({
    topic,
    params: listParams,
    fetchPage: (cursor) => fetchMessages(api, kind, node, filter, cursor),
  })
  const items = messages.rows
  const list = messages.query
  const matches = (row: MessageRow) => messageMatches(row, filter)
  const viewId = `messaging.${kind}`
  const frame = usePageFrame({ viewId, rows: items, matches })
  const newRows = useNewRows<MessageRow>(topic, matches)
  const pending = usePending()
  const [allMatching, setAllMatching] = useState(false)
  // A live change can move a checked row out of the list while the cache still holds it: the bar counts and acts on
  // the checked rows the list shows, never on hidden ones.
  const checkedRows = items.filter((row) => frame.checked.has(row.id) && matches(row))
  // The version each refused row had when it was refused; a later change to the row makes the refusal out of date.
  const refusedAt = useRef<ReadonlyMap<string, string>>(new Map())
  const listId = listName(filter)
  const state = describe(kind, listId)
  const failed = listId === 'failed'
  const scoped = canScope(filter, kind)

  useNodeReset()

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

  function setFilter(values: Partial<MessageFilter>) {
    update((next) => {
      for (const [key, set] of Object.entries(values)) {
        if (set) {
          next.set(key, set)
        } else {
          next.delete(key)
        }
      }
    })
  }

  // The list itself, chosen in the sidebar, is its status and flags; clearing the filters keeps them.
  function clearFilters() {
    update((next) => (['store', 'consumer', 'name', 'fingerprint'] as const).forEach((name) => next.delete(name)))
  }

  // A new filter or node is a new list, whether the header, the sidebar, a link, or Back changed it: the checked rows,
  // and so `Select all matching` and the refusals, answered for the old one.
  useEffect(() => frame.setChecked(new Set()), [filterKey])

  // `Select all matching` answers for the rows checked when it was pressed; a change to them ends it.
  useEffect(() => setAllMatching(false), [frame.checked])

  // A checked row a live change moved out of the list leaves the selection, so it does not come back checked; while
  // every matching row is selected the Operation scopes by the filter, so the checked rows stay. A refused row that
  // changed since is answered afresh by the next action, so its tag goes.
  useEffect(() => {
    const left = allMatching ? [] : items.filter((row) => frame.checked.has(row.id) && !matches(row))

    if (left.length > 0) {
      const next = new Set(frame.checked)
      left.forEach((row) => next.delete(row.id))
      frame.setChecked(next)

      return
    }

    const changed = new Set(
      items.filter((row) => frame.refused.has(row.id) && refusedAt.current.get(row.id) !== (row.version ?? '')).map((row) => row.id),
    )

    if (changed.size > 0) {
      frame.setRefused(new Map([...frame.refused].filter(([id]) => !changed.has(id))))
    }
  }, [items])

  const count = messages.count
  const narrowed = (['store', 'consumer', 'name', 'fingerprint'] as const).some((name) => filter[name] !== '')
  const openRow = items.find((row) => row.id === frame.openId)
  const all = listId === 'all'
  // The filters a link set that the header shows as chips to clear: on a state list, every one; on All, those with no
  // select. On the Failed list a pressed cause chip already shows its fingerprint and its consumer or store.
  const chipped: readonly { name: keyof MessageFilter; label: string }[] = [
    ...(all ? [] : [{ name: 'store', label: 'store' } as const, { name: 'consumer', label: 'consumer' } as const, { name: 'name', label: 'message' } as const]),
    { name: 'fingerprint', label: 'cause' },
  ]
  const setFilters = chipped.filter(({ name }) => filter[name] && !(failed && filter.fingerprint && (name === 'fingerprint' || name === 'consumer' || name === 'store')))
  const unavailable = unavailableStores(list.data?.pages[0])

  let panel
  if (frame.openId) {
    panel = openRow ? <MessagePanel kind={kind} node={node} row={openRow} pending={pending} /> : <LookedUp kind={kind} node={node} id={frame.openId} pending={pending} />
  }

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title={state.title}
          tone={all ? undefined : rowMark(listId === 'held' ? { isHeld: true, terminal: false } : { status: filter.status || 'Failed', terminal: !filter.retrying }).tone}
          count={count ? format.count(count) : undefined}
          description={state.description}
          actions={
            allMatching && count ? (
              <MatchingActions kind={kind} filter={filter} node={node} count={format.count(count)} />
            ) : checkedRows.length > 0 ? (
              <CheckedActions
                kind={kind}
                rows={checkedRows}
                pending={pending}
                frame={frame}
                onRefused={(reasons, versions) => {
                  refusedAt.current = versions
                  frame.setRefused(reasons)
                }}
              />
            ) : null
          }
        >
          <NodeSwitch />
          {failed ? <FailureChips kind={kind} node={node} filter={filter} count={count ? format.count(count) : undefined} onPick={setFilter} /> : null}
          {/* A state list keeps its header to one row, as the sidebar already chose the state: its other filters
              arrive by link and show as chips to clear. The All list offers every filter. */}
          {all ? <ListFilters kind={kind} filter={filter} onPick={setFilter} /> : null}
          {setFilters.length > 0 ? (
            <FilterChips label="Set filters">
              {setFilters.map(({ name, label }) => (
                <FilterChip key={name} label={label} value={filter[name]} onClear={() => setFilter({ [name]: '' })} />
              ))}
            </FilterChips>
          ) : null}
        </PageHeader>
      }
      panel={panel}
    >
      <StaleNotice what={`these ${kind} messages`} onRefresh={() => void list.refetch()} />
      {unavailable.length > 0 ? (
        <Banner role="status">
          {`Some stores did not answer, so their messages are missing: ${unavailable.join(', ')}.`}
        </Banner>
      ) : null}
      <QueryState query={list} label={`Loading ${kind} messages`} rows={6}>
        {() => (
          <MessagesTable
            // A new filter is a new list: the held rows and the scroll start again.
            key={filterKey}
            kind={kind}
            viewId={viewId}
            frame={frame}
            messages={messages}
            matches={matches}
            newRows={newRows}
            failed={failed}
            pending={pending}
            // An Operation's scope cannot say retrying or held, so those lists act on the checked rows only.
            onSelectAllMatching={scoped ? () => setAllMatching(true) : undefined}
            emptyState={
              narrowed ? (
                <span>
                  {`No ${kind} messages match. `}
                  <Button variant="ghost" onPress={clearFilters}>
                    Clear filters
                  </Button>
                </span>
              ) : all ? (
                `No ${kind} messages yet. A row appears once a host ${kind === 'published' ? 'publishes' : 'receives'} a message.`
              ) : (
                `No ${state.title.toLowerCase()} ${kind} messages.`
              )
            }
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

/**
 * The stores the first page says did not answer. The keyset list keeps each page as the fetch returned it, typed as
 * the SDK's page, so the extra field is read back by checking for it.
 */
function unavailableStores(page: object | undefined): string[] {
  return page && 'unavailable' in page && Array.isArray(page.unavailable) ? page.unavailable.filter((store): store is string => typeof store === 'string') : []
}

/** Options from served names, keeping a value a link set that the server no longer lists. */
function optionsOf(names: readonly string[], current: string) {
  return [...new Set([...names, ...(current ? [current] : [])])].sort().map((value) => ({ value, label: value }))
}

/** The All list's selects: the store or consumer, and the message name, as the node's stores, consumers, and contracts serve them. */
function ListFilters({ kind, filter, onPick }: { kind: MessageKind; filter: MessageFilter; onPick: (values: Partial<MessageFilter>) => void }) {
  const nodes = useNodes()
  const { node, local } = useNode()
  const consumers = useConsumers()
  const contracts = useContracts()
  const served = nodes.data?.items.find((item) => (node ? item.name === node : item.isLocal)) ?? local

  return (
    <>
      {kind === 'published' ? (
        <SelectField
          label="Store"
          emptyLabel="Any store"
          value={filter.store}
          onChange={(value) => onPick({ store: value })}
          options={optionsOf(served?.stores ?? [], filter.store)}
        />
      ) : (
        <SelectField
          label="Consumer"
          emptyLabel="Any consumer"
          value={filter.consumer}
          onChange={(value) => onPick({ consumer: value })}
          options={optionsOf((consumers.data ?? []).map((consumer) => consumer.identity), filter.consumer)}
        />
      )}
      <SelectField
        label="Message"
        emptyLabel="Any message"
        value={filter.name}
        onChange={(value) => onPick({ name: value })}
        // Contracts list only the messages the node declares to publish, so a consume-only message name comes from its consumer.
        options={optionsOf(
          [...(contracts.data ?? []).map((contract) => contract.name), ...(consumers.data ?? []).flatMap((consumer) => consumer.messages.map((message) => message.name))],
          filter.name,
        )}
      />
    </>
  )
}

interface MessagesTableProps {
  kind: MessageKind
  viewId: string
  frame: ReturnType<typeof usePageFrame>
  messages: ReturnType<typeof useDashboardKeysetList<MessageRow>>
  matches: (row: MessageRow) => boolean
  newRows: ReturnType<typeof useNewRows<MessageRow>>
  failed: boolean
  pending: Pending
  onSelectAllMatching?: () => void
  emptyState: ReactNode
}

function MessagesTable({ kind, viewId, frame, messages, matches, newRows, failed, pending, onSelectAllMatching, emptyState }: MessagesTableProps) {
  const format = useFormat()
  // The server serves a failure's fingerprint, not its exception, on a list row.
  const causeColumn: DataTableColumn<MessageRow> = {
    id: 'cause',
    header: 'Cause',
    cell: (row) => (
      <CellText title={row.failureFingerprint}>
        <Mono>{row.failureFingerprint ?? ''}</Mono>
      </CellText>
    ),
    priority: 2,
    width: '1.5fr',
  }
  const consumerColumn: DataTableColumn<MessageRow> = {
    id: 'consumer',
    header: 'Consumer',
    cell: (row) => (
      <CellText title={row.consumer}>
        <Mono>{row.consumer ?? ''}</Mono>
      </CellText>
    ),
    priority: 2,
    width: '2fr',
  }
  const storeColumn: DataTableColumn<MessageRow> = {
    id: 'store',
    header: 'Store',
    cell: (row) => <CellText title={row.store}>{row.store}</CellText>,
    priority: 2,
    width: '1fr',
  }

  return (
    <DataTable
      label={kind === 'published' ? 'Published messages' : 'Received messages'}
      viewId={viewId}
      frame={frame}
      rows={messages.rows}
      matches={matches}
      hasMore={messages.hasMore}
      isLoadingMore={messages.isLoadingMore}
      onLoadMore={messages.loadMore}
      newRows={newRows}
      selectionMode="multiple"
      matchingCount={onSelectAllMatching ? messages.count?.value : undefined}
      onSelectAllMatching={onSelectAllMatching}
      emptyState={emptyState}
      columns={[
        {
          id: 'name',
          header: 'Message',
          cell: (row) => (
            <CellText title={row.name}>
              <Mono>{row.name ?? '(unnamed)'}</Mono>
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
              <StatusMark {...rowMark(row, pending.labelOf(row))} />
            </CellText>
          ),
          width: 130,
        },
        ...(kind === 'received' ? [consumerColumn] : [storeColumn]),
        ...(failed ? [causeColumn] : []),
        { id: 'retries', header: 'Retries', cell: (row) => format.number(row.retries), align: 'end', priority: 2, width: 80 },
        {
          id: 'added',
          header: failed ? 'Failed at' : 'Added',
          cell: (row) => (
            <CellText>
              <Time value={failed ? (row.changedAt ?? row.added) : row.added} short />
            </CellText>
          ),
          width: 110,
        },
      ]}
    />
  )
}

/** The part of a `v{n}:{token}` fingerprint a chip shows: a 16-character hash shortened to 6, a fixed cause whole. */
function shortFingerprint(fingerprint: string): string {
  const token = fingerprint.slice(fingerprint.indexOf(':') + 1)

  return /^[0-9a-f]{16}$/.test(token) ? token.slice(0, 6) : token
}

/**
 * The Failed list's causes: `All`, then one chip per failure group the server counts on this node, labelled by its
 * consumer or store and its fingerprint; the chip's title names the whole fingerprint.
 */
function FailureChips({
  kind,
  node,
  filter,
  count,
  onPick,
}: {
  kind: MessageKind
  node: string
  filter: MessageFilter
  count?: string
  onPick: (values: Partial<MessageFilter>) => void
}) {
  const format = useFormat()
  const { local } = useNode()
  const groups = useMessagingAttention().items.flatMap((item) => {
    const group = parseFailureGroup(item.id)
    // The local node's groups carry no node; a peer's carry its name, which may also be this node's own.
    const groupNode = group && local && group.node === local.name ? '' : group?.node

    // A failure group carries no readable cause, and one consumer can fail several ways, so each chip also names its
    // fingerprint: the first characters of a computed hash, or a fixed cause such as `expired` whole.
    return group && group.kind === kind && groupNode === node && group.fingerprint
      ? [
          {
            id: item.id,
            group,
            label: `${(kind === 'received' ? group.consumer : group.store) || 'unknown'} · ${shortFingerprint(group.fingerprint)}`,
            count: { value: item.count, capped: item.capped },
          },
        ]
      : []
  })

  return (
    <FilterChips label="Filter by cause">
      <FilterChip label="All" count={count} pressed={!filter.fingerprint} onPress={() => onPick({ fingerprint: '', consumer: '', store: '' })} />
      {groups.map(({ id, group, label, count: groupCount }) => {
        // Two consumers can fail the same way, so a chip is its pair: one consumer or store and one fingerprint.
        const pressed =
          filter.fingerprint === group.fingerprint && (kind === 'received' ? filter.consumer === group.consumer : filter.store === group.store)
        const scope = kind === 'received' ? { consumer: pressed ? '' : group.consumer } : { store: pressed ? '' : group.store }

        return (
          <FilterChip
            key={id}
            label={label}
            title={`${label}, fingerprint ${group.fingerprint}`}
            count={format.count(groupCount)}
            failing
            pressed={pressed}
            onPress={() => onPick({ fingerprint: pressed ? '' : group.fingerprint, ...scope })}
          />
        )
      })}
    </FilterChips>
  )
}

function CheckedActions({
  kind,
  rows,
  pending,
  frame,
  onRefused,
}: {
  kind: MessageKind
  rows: readonly MessageRow[]
  pending: Pending
  frame: ReturnType<typeof usePageFrame>
  onRefused: (reasons: ReadonlyMap<string, string>, versions: ReadonlyMap<string, string>) => void
}) {
  const module = useModule()
  const commands = useMessagingCommands()
  const allowed = useAllowedActions(kind, rows)
  const ids = rows.map((row) => row.id)
  // Captured at render, so the versions are the ones the operator saw when they ran the command.
  const versions = versionsOf(rows)
  // Hold and release each apply to some rows only; the bar offers them when one checked row can take them.
  const names = rowCommands[kind].filter(({ name }) => rows.some((row) => offers(row, name)))

  return (
    <>
      <span>{`${rows.length} selected`}</span>
      {names.map(({ name, variant }) => {
        const command: CommandDefinition | undefined = commands.find(name)

        return command ? (
          <CommandAction
            key={name}
            module={module.id}
            command={command}
            ids={ids}
            noun={ids.length === 1 ? 'message' : 'messages'}
            variant={variant}
            commands={commands.all}
            refused={allowed.refused(command.id)}
            onRefused={(reasons) => onRefused(reasons, versions)}
            onViewRefused={frame.reveal}
            onDone={markDone(pending, versions, name)}
          />
        ) : null
      })}
    </>
  )
}

/** Every row the filter matches, past the loaded page: each action runs over them as an Operation on this node. */
function MatchingActions({ kind, filter, node, count }: { kind: MessageKind; filter: MessageFilter; node: string; count: string }) {
  const module = useModule()
  const { start } = useOperations()
  const commands = useMessagingCommands()
  const noun = messagesNoun(kind, filter, node)

  return (
    <>
      <span>{`All ${count} matching ${noun}`}</span>
      {operationCommands[kind].map(({ name, variant }) => {
        const command = commands.find(name)

        return command ? (
          <Button
            key={command.id}
            variant={variant}
            disabledReason={command.canRun ? null : (command.disabledReason ?? viewOnlyReason)}
            onPress={() => start({ module: module.id, command, filter: operationScope(filter, node), noun })}
          >
            {`${command.label} ${count}`}
          </Button>
        ) : null
      })}
    </>
  )
}

function LookedUp({ kind, node, id, pending }: { kind: MessageKind; node: string; id: string; pending: Pending }) {
  const { api } = useDashboard()
  const detail = useDashboardObject({ topic: topicOf(kind, node), id, fetch: async () => (await fetchMessage(api, kind, node, id)).row })

  if (detail.data) {
    return <MessagePanel kind={kind} node={node} row={detail.data} pending={pending} />
  }

  return (
    <DetailPanel label={`Message ${id}`} name="Message" id={id}>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <p role="alert">This message no longer exists.</p>
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label="Loading the message" />
      )}
    </DetailPanel>
  )
}
