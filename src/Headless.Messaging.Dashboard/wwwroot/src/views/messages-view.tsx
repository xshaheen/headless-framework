import { useDashboard, useModule } from '@headless/dashboard-sdk'
import {
  CommandAction,
  useAllowedActions,
  useOperations,
  type CommandDefinition,
} from '@headless/dashboard-sdk/commands'
import {
  Announcer,
  Banner,
  Button,
  DetailPanel,
  EmptyState,
  FailureReason,
  FilterChip,
  FilterChips,
  FilterSelect,
  FilterToolbar,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  shortTypeName,
  Skeleton,
  splitFailure,
  StatusMark,
  usePageFrame,
  Timestamp,
  QueryState,
} from '@headless/dashboard-sdk/components'
import { FilterCombobox } from '@headless/dashboard-sdk/combobox'
import {
  DashboardApiError,
  isNewerVersion,
  useDashboardKeysetList,
  useDashboardObject,
  useNewRows,
} from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DateRangeField, timeRangePresets, useListRange, useTimeRangeSearch } from '@headless/dashboard-sdk/date-range'
import {
  DataTable,
  SearchField,
  type DataTableColumn,
  type ListRange,
  type ListSort,
} from '@headless/dashboard-sdk/table'
import { PublishedIcon, ReceivedIcon, type IconComponent } from '@headless/dashboard-sdk/icons'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  CellText,
  MessagePanel,
  MessagingStats,
  NodeSwitch,
  StaleNotice,
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
  messageLanes,
  messageMatches,
  messagesNoun,
  noSearch,
  operationScope,
  parseFailureGroup,
  readFilter,
  rowMark,
  searchKey,
  topicOf,
  useMessagingCommands,
  usePending,
  versionsOf,
  viewOnlyReason,
  type MessageCommandName,
  type MessageFilter,
  type MessageKind,
  type MessageRow,
  type MessageSearch,
  type Pending,
} from '../messages.ts'

const laneOptions = messageLanes.map((lane) => ({ value: lane, label: lane }))

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
      return {
        title: 'Succeeded',
        description: kind === 'received' ? 'Consumed without an exception.' : 'Handed to the broker.',
      }
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
const operationCommands: Readonly<
  Record<MessageKind, readonly { name: MessageCommandName; variant?: 'primary' | 'danger' }[]>
> = {
  received: [
    { name: 're-execute', variant: 'primary' },
    { name: 'purge', variant: 'danger' },
  ],
  published: [
    { name: 're-send', variant: 'primary' },
    { name: 'delete', variant: 'danger' },
  ],
}

export function MessagesView({ kind }: { kind: MessageKind }) {
  const { api } = useDashboard()
  const format = useFormat()
  const [params, setParams] = useSearchParams()
  const { node } = useNode()
  const filter = readFilter(params, kind)
  // The range is in the URL, so a link keeps it; the search and the sort are this view's own state, so search text
  // never reaches the URL, and a reload starts without it.
  const [timeRange, setTimeRange] = useTimeRangeSearch({ presets: timeRangePresets })
  const range = useListRange(timeRange)
  const [search, setSearch] = useState<MessageSearch>(noSearch)
  const [sort, setSort] = useState<ListSort>()
  // Bumped to empty the search boxes, which keep their own text.
  const [searchReset, setSearchReset] = useState(0)
  const isSearching = searchKey(search) !== ''
  const filterKey = [
    node,
    ...filterNames.map((name) => filter[name]),
    searchKey(search),
    range?.start,
    range?.end,
  ].join('\n')
  const topic = topicOf(kind, node)
  const listParams = { ...filter, node }
  // The Failed list's time column shows when a row failed, so an added-time order chosen on another list does not apply.
  const listSort = filter.status === 'Failed' && sort?.key === 'added' ? undefined : sort
  const messages = useDashboardKeysetList<MessageRow>({
    topic,
    params: listParams,
    sort: listSort,
    search: searchKey(search),
    range,
    fetchPage: (cursor, request) => fetchMessages(api, kind, node, filter, cursor, request),
  })
  const items = messages.rows
  const list = messages.query
  const firstPage = pageFacts(list.data?.pages[0])
  // A row carries no content, so the browser cannot tell whether a changed row still matches a search: while one is
  // set, the list keeps to the rows the server matched.
  const searched = isSearching
    ? new Set(list.data?.pages.flatMap((page) => page.items.map((row) => row.id)))
    : undefined
  const matches = (row: MessageRow) => messageMatches(row, filter, range) && (!searched || searched.has(row.id))
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
    setSearch(noSearch)
    setSearchReset((count) => count + 1)
    update((next) =>
      (['store', 'consumer', 'name', 'fingerprint', 'lane', 'messageId', 'range', 'from', 'to'] as const).forEach(
        (name) => next.delete(name),
      ),
    )
  }

  function searchFor(values: Partial<MessageSearch>) {
    setSearch((current) => ({ ...current, ...values }))
  }

  function clearSearch(name: keyof MessageSearch) {
    searchFor({ [name]: '' })
    setSearchReset((count) => count + 1)
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
      items
        .filter((row) => frame.refused.has(row.id) && isNewerVersion(row.version, refusedAt.current.get(row.id)))
        .map((row) => row.id),
    )

    if (changed.size > 0) {
      frame.setRefused(new Map([...frame.refused].filter(([id]) => !changed.has(id))))
    }
  }, [items])

  const count = messages.count
  const incomplete = firstPage.incomplete
  const narrowed =
    (['store', 'consumer', 'name', 'fingerprint', 'lane', 'messageId'] as const).some((name) => filter[name] !== '') ||
    isSearching ||
    range !== undefined
  const openRow = items.find((row) => row.id === frame.openId)
  const all = listId === 'all'
  // The filters a link set that the header shows as chips to clear: on a state list, every one; on All, those with no
  // select. On the Failed list a pressed cause chip already shows its fingerprint and its consumer or store.
  const chipped: readonly { name: keyof MessageFilter; label: string }[] = [
    ...(all
      ? []
      : [
          { name: 'store', label: 'store' } as const,
          { name: 'consumer', label: 'consumer' } as const,
          { name: 'name', label: 'message' } as const,
        ]),
    { name: 'fingerprint', label: 'cause' },
    { name: 'messageId', label: 'message id' },
    { name: 'lane', label: 'lane' },
  ]
  const setFilters = chipped.filter(
    ({ name }) =>
      filter[name] &&
      !(failed && filter.fingerprint && (name === 'fingerprint' || name === 'consumer' || name === 'store')),
  )
  const unavailable = firstPage.unavailable
  const searchChips = (
    [
      { name: 'content', label: 'content' },
      { name: 'correlationId', label: 'correlation' },
    ] as const
  ).filter(({ name }) => search[name])

  let panel
  if (frame.openId) {
    panel = openRow ? (
      <MessagePanel kind={kind} node={node} row={openRow} pending={pending} />
    ) : (
      <LookedUp kind={kind} node={node} id={frame.openId} pending={pending} />
    )
  }

  return (
    <PageFrame
      frame={frame}
      stats={<MessagingStats />}
      header={
        <PageHeader
          title={state.title}
          tone={
            all
              ? undefined
              : rowMark(
                  listId === 'held'
                    ? { isHeld: true, terminal: false }
                    : { status: filter.status || 'Failed', terminal: !filter.retrying },
                ).tone
          }
          count={count ? format.count(count) : undefined}
          description={state.description}
          toolbar={
            <FilterToolbar
              active={
                (['store', 'consumer', 'name', 'lane', 'messageId'] as const).filter((name) => filter[name]).length +
                (timeRange ? 1 : 0) +
                (search.correlationId ? 1 : 0)
              }
              chips={
                <>
                  {failed ? (
                    <FailureChips
                      kind={kind}
                      node={node}
                      filter={filter}
                      rows={items}
                      count={count ? format.count(count) : undefined}
                      onPick={setFilter}
                    />
                  ) : null}
                  {setFilters.length > 0 || searchChips.length > 0 ? (
                    <FilterChips label="Set filters">
                      {setFilters.map(({ name, label }) => (
                        <FilterChip
                          key={name}
                          label={label}
                          value={filter[name]}
                          onClear={() => setFilter({ [name]: '' })}
                        />
                      ))}
                      {searchChips.map(({ name, label }) => (
                        <FilterChip key={name} label={label} value={search[name]} onClear={() => clearSearch(name)} />
                      ))}
                    </FilterChips>
                  ) : null}
                </>
              }
              search={
                // Restarted with the current text, so clearing a chip empties its box.
                <SearchField
                  key={`content-${searchReset}`}
                  label={`Search ${kind} message content`}
                  placeholder="Search content"
                  mode="submit"
                  compact
                  defaultValue={search.content}
                  onSearch={(content) => searchFor({ content })}
                />
              }
              // The rarer filters sit behind More filters, so the row stays one line beside the title at 1024 px.
              more={
                <>
                  <FilterSelect
                    label="Lane"
                    emptyLabel="Any lane"
                    value={filter.lane}
                    onChange={(value) => setFilter({ lane: value as MessageFilter['lane'] })}
                    options={laneOptions}
                  />
                  <DateRangeField
                    label="Added"
                    value={timeRange}
                    onChange={setTimeRange}
                    presets={timeRangePresets}
                    compact
                  />
                  <SearchField
                    key={`message-id-${searchReset}-${filter.messageId}`}
                    label="Message id"
                    placeholder="Exact message id"
                    mode="submit"
                    compact
                    defaultValue={filter.messageId}
                    onSearch={(messageId) => setFilter({ messageId })}
                  />
                  <SearchField
                    key={`correlation-${searchReset}`}
                    label="Correlation id"
                    placeholder="Exact correlation id"
                    mode="submit"
                    compact
                    defaultValue={search.correlationId}
                    onSearch={(correlationId) => searchFor({ correlationId })}
                  />
                </>
              }
            >
              <NodeSwitch />
              {/* A state list keeps only the node, as the sidebar already chose the state: its other filters arrive
                  by link and show as chips to clear. The All list offers every filter. */}
              {all ? <ListFilters kind={kind} filter={filter} onPick={setFilter} /> : null}
            </FilterToolbar>
          }
          actions={
            allMatching && count ? (
              <MatchingActions
                kind={kind}
                filter={filter}
                node={node}
                range={range}
                search={search}
                count={format.count(count)}
              />
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
        />
      }
      panel={panel}
    >
      <StaleNotice what={`these ${kind} messages`} onRefresh={() => void list.refetch()} failed={list.isError} />
      {/* Said from a region that stays in the page: a banner that brought its own live region could go unheard. */}
      <Announcer
        message={[
          unavailable.length > 0 ? 'Some stores did not answer.' : '',
          incomplete ? 'Search did not finish.' : '',
        ]
          .filter(Boolean)
          .join(' ')}
      />
      {unavailable.length > 0 ? (
        <Banner>{`Some stores did not answer, so their messages are missing: ${unavailable.join(', ')}.`}</Banner>
      ) : null}
      {incomplete ? (
        <Banner>
          <span>Search did not finish; results may be missing.</span>
          <Button variant="ghost" onPress={() => void list.refetch()}>
            Retry
          </Button>
        </Banner>
      ) : null}
      <QueryState query={list} label={`Loading ${kind} messages`} rows={6} shape="table">
        {() => (
          <MessagesTable
            // A new filter is a new list: the held rows and the scroll start again.
            key={filterKey}
            kind={kind}
            frame={frame}
            messages={messages}
            matches={matches}
            newRows={newRows}
            failed={failed}
            pending={pending}
            sort={listSort}
            onSortChange={setSort}
            // An Operation's scope cannot say retrying or held, so those lists act on the checked rows only.
            onSelectAllMatching={scoped ? () => setAllMatching(true) : undefined}
            emptyIcon={kind === 'published' ? PublishedIcon : ReceivedIcon}
            emptyTitle={
              incomplete
                ? 'No match found in time'
                : narrowed
                  ? `No ${kind} messages match`
                  : all
                    ? `No ${kind} messages yet`
                    : `No ${state.title.toLowerCase()} ${kind} messages`
            }
            emptyState={
              incomplete
                ? // A search out of time that found nothing yet is not the same as no match.
                  'The search ran out of time before it found a match; results may be missing.'
                : narrowed
                  ? 'Clear the filters to see every row.'
                  : all
                    ? `A row appears once a host ${kind === 'published' ? 'publishes' : 'receives'} a message.`
                    : 'Nothing is in this state right now.'
            }
            emptyAction={narrowed && !incomplete ? <Button onPress={clearFilters}>Clear filters</Button> : undefined}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

/**
 * What the first page says beyond its rows: the stores that did not answer, and whether a search ran out of time. The
 * keyset list keeps each page as the fetch returned it, typed as the SDK's page, so the extra fields are read back by
 * checking for them.
 */
function pageFacts(page: object | undefined): { unavailable: string[]; incomplete: boolean } {
  const unavailable =
    page && 'unavailable' in page && Array.isArray(page.unavailable)
      ? page.unavailable.filter((store): store is string => typeof store === 'string')
      : []

  return { unavailable, incomplete: page !== undefined && 'incomplete' in page && page.incomplete === true }
}

/** Options from served names, keeping a value a link set that the server no longer lists. */
function optionsOf(names: readonly string[], current: string) {
  return [...new Set([...names, ...(current ? [current] : [])])].sort().map((value) => ({ value, label: value }))
}

/** The All list's selects: the store or consumer, and the message name, as the node's stores, consumers, and contracts serve them. */
function ListFilters({
  kind,
  filter,
  onPick,
}: {
  kind: MessageKind
  filter: MessageFilter
  onPick: (values: Partial<MessageFilter>) => void
}) {
  const nodes = useNodes()
  const { node, local } = useNode()
  const consumers = useConsumers()
  const contracts = useContracts()
  const served = nodes.data?.items.find((item) => (node ? item.name === node : item.isLocal)) ?? local

  return (
    <>
      {kind === 'published' ? (
        <FilterSelect
          label="Store"
          emptyLabel="Any store"
          value={filter.store}
          onChange={(value) => onPick({ store: value })}
          options={optionsOf(served?.stores ?? [], filter.store)}
        />
      ) : (
        <FilterCombobox
          label="Consumer"
          emptyLabel="Any consumer"
          value={filter.consumer}
          onChange={(value) => onPick({ consumer: value })}
          options={optionsOf(
            (consumers.data ?? []).map((consumer) => consumer.identity),
            filter.consumer,
          )}
        />
      )}
      <FilterCombobox
        label="Message"
        emptyLabel="Any message"
        value={filter.name}
        onChange={(value) => onPick({ name: value })}
        // Contracts list only the messages the node declares to publish, so a consume-only message name comes from its consumer.
        options={optionsOf(
          [
            ...(contracts.data ?? []).map((contract) => contract.name),
            ...(consumers.data ?? []).flatMap((consumer) => consumer.messages.map((message) => message.name)),
          ],
          filter.name,
        )}
      />
    </>
  )
}

interface MessagesTableProps {
  kind: MessageKind
  frame: ReturnType<typeof usePageFrame>
  messages: ReturnType<typeof useDashboardKeysetList<MessageRow>>
  matches: (row: MessageRow) => boolean
  newRows: ReturnType<typeof useNewRows<MessageRow>>
  failed: boolean
  pending: Pending
  sort: ListSort | undefined
  onSortChange: (sort: ListSort) => void
  onSelectAllMatching?: () => void
  emptyState: ReactNode
  emptyIcon: IconComponent
  emptyTitle: ReactNode
  emptyAction?: ReactNode
}

function MessagesTable({
  kind,
  frame,
  messages,
  matches,
  newRows,
  failed,
  pending,
  sort,
  onSortChange,
  onSelectAllMatching,
  emptyState,
  emptyIcon,
  emptyTitle,
  emptyAction,
}: MessagesTableProps) {
  const format = useFormat()
  // A failed row reads by what it failed with. The fingerprint that groups its cause stands in for a row whose
  // stored failure the server could not read, so the cell still tells two causes apart.
  const reasonColumn: DataTableColumn<MessageRow> = {
    id: 'reason',
    header: 'Reason',
    cell: (row) => {
      const reason = failureOf(row)

      if (reason) {
        return <FailureReason {...reason} />
      }

      return row.failureFingerprint ? (
        <CellText title={row.failureFingerprint}>
          <Mono>{row.failureFingerprint}</Mono>
        </CellText>
      ) : undefined
    },
    priority: 2,
    width: '4fr',
  }
  const consumerColumn: DataTableColumn<MessageRow> = {
    id: 'consumer',
    header: 'Consumer',
    cell: (row) => (
      <CellText title={row.consumer}>
        <Mono>{row.consumer ?? ''}</Mono>
      </CellText>
    ),
    kind: 'id',
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
      emptyIcon={emptyIcon}
      emptyTitle={emptyTitle}
      emptyAction={emptyAction}
      sort={sort}
      onSortChange={onSortChange}
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
          kind: 'id',
          width: '2fr',
          sortKey: 'name',
        },
        {
          // Two messages of one name read apart by their ids.
          id: 'messageId',
          header: 'Message id',
          cell: (row) => (
            <CellText title={row.messageId}>
              <Mono>{idTail(row.messageId)}</Mono>
            </CellText>
          ),
          kind: 'id',
          width: '1.5fr',
        },
        {
          id: 'status',
          header: 'Status',
          cell: (row) => (
            <CellText>
              <StatusMark {...rowMark(row, pending.labelOf(row))} />
            </CellText>
          ),
          kind: 'status',
          width: 130,
        },
        ...(kind === 'received' ? [consumerColumn] : [storeColumn]),
        ...(failed ? [reasonColumn] : []),
        {
          id: 'retries',
          header: 'Retries',
          cell: (row) => format.number(row.retries),
          kind: 'number',
          priority: 2,
          width: 80,
        },
        {
          id: 'added',
          header: failed ? 'Failed at' : 'Added',
          cell: (row) => (
            <CellText>
              <Timestamp value={failed ? (row.changedAt ?? row.added) : row.added} />
            </CellText>
          ),
          kind: 'time',
          width: 110,
          // The Failed list shows when each row failed, while the server sorts by when it was added.
          ...(failed ? {} : { sortKey: 'added' }),
        },
        // The Failed list gives this column's width to the reason: the row's relative time already shows the exact
        // one on hover, focus, and press, and what failed is what tells two failed rows apart.
        ...(failed
          ? []
          : [
              {
                id: 'exact',
                header: 'Added at',
                cell: (row: MessageRow) => (
                  <CellText title={format.exactTime(row.added)}>
                    <Mono>{format.exactTime(row.added)}</Mono>
                  </CellText>
                ),
                kind: 'time' as const,
                priority: 2 as const,
                width: 250,
              },
            ]),
      ]}
    />
  )
}

/**
 * The end of a message id, which is where two ids differ: a time-ordered id shares its start with every message of
 * the same second, so a cell cut at its end showed one prefix down the whole column. The full id is the cell's title.
 */
function idTail(id: string | undefined): string {
  if (!id) {
    return ''
  }

  // A short id, such as an application's own `order-8452`, fits the cell and reads whole.
  return id.length > 20 ? `…${id.slice(-8)}` : id
}

/**
 * What a row failed with, as its type and message. A live change can leave an earlier reason on a row that has moved
 * on, so only a failed row has one.
 */
function failureOf(row: Pick<MessageRow, 'status' | 'reason'>) {
  return row.status === 'Failed' && row.reason ? splitFailure(row.reason) : undefined
}

/** The part of a `v{n}:{token}` fingerprint a chip shows: a 16-character hash shortened to 6, a fixed cause whole. */
function shortFingerprint(fingerprint: string): string {
  const token = fingerprint.slice(fingerprint.indexOf(':') + 1)

  return /^[0-9a-f]{16}$/.test(token) ? token.slice(0, 6) : token
}

/**
 * The Failed list's causes: `All`, then one chip per failure group the server counts on this node, labelled by its
 * consumer or store and its cause; the chip's title names the whole fingerprint.
 */
function FailureChips({
  kind,
  node,
  filter,
  rows,
  count,
  onPick,
}: {
  kind: MessageKind
  node: string
  filter: MessageFilter
  /** The loaded rows, which name a group's cause by its exception type once one of the group's rows is among them. */
  rows: readonly MessageRow[]
  count?: string
  onPick: (values: Partial<MessageFilter>) => void
}) {
  const format = useFormat()
  const { local } = useNode()
  const groups = useMessagingAttention().items.flatMap((item) => {
    const group = parseFailureGroup(item.id)
    // The local node's groups carry no node; a peer's carry its name, which may also be this node's own.
    const groupNode = group && local && group.node === local.name ? '' : group?.node

    // One consumer can fail several ways, so each chip also names its cause. A failure group serves only the
    // fingerprint, so the cause reads as the exception type of a loaded row with that fingerprint, and until one is
    // loaded as the fingerprint itself: the first characters of a computed hash, or a fixed cause such as `expired`.
    return group && group.kind === kind && groupNode === node && group.fingerprint
      ? [
          {
            id: item.id,
            group,
            label: `${(kind === 'received' ? group.consumer : group.store) || 'unknown'} · ${causeOf(group.fingerprint, rows)}`,
            count: { value: item.count, capped: item.capped },
          },
        ]
      : []
  })

  return (
    <FilterChips label="Filter by cause">
      <FilterChip
        label="All"
        count={count}
        pressed={!filter.fingerprint}
        onPress={() => onPick({ fingerprint: '', consumer: '', store: '' })}
      />
      {groups.map(({ id, group, label, count: groupCount }) => {
        // Two consumers can fail the same way, so a chip is its pair: one consumer or store and one fingerprint.
        const pressed =
          filter.fingerprint === group.fingerprint &&
          (kind === 'received' ? filter.consumer === group.consumer : filter.store === group.store)
        const scope =
          kind === 'received' ? { consumer: pressed ? '' : group.consumer } : { store: pressed ? '' : group.store }

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

/** A fingerprint's cause in words: the exception type a loaded row failed with, else the short fingerprint. */
function causeOf(fingerprint: string, rows: readonly MessageRow[]) {
  const type = rows
    .flatMap((row) => (row.failureFingerprint === fingerprint ? [failureOf(row)?.type] : []))
    .find(Boolean)

  return type ? shortTypeName(type) : shortFingerprint(fingerprint)
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
function MatchingActions({
  kind,
  filter,
  node,
  range,
  search,
  count,
}: {
  kind: MessageKind
  filter: MessageFilter
  node: string
  range: ListRange | undefined
  search: MessageSearch
  count: string
}) {
  const module = useModule()
  const { start } = useOperations()
  const commands = useMessagingCommands()
  // The confirm and the progress name the search, never its text: both stay on screen while the Operation runs.
  const noun = `${messagesNoun(kind, filter, node)}${searchKey(search) ? ' that match the search' : ''}`

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
            onPress={() =>
              start({ module: module.id, command, filter: operationScope(filter, node, range, search), noun })
            }
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
  const detail = useDashboardObject({
    topic: topicOf(kind, node),
    id,
    fetch: async () => (await fetchMessage(api, kind, node, id)).row,
  })

  if (detail.data) {
    return <MessagePanel kind={kind} node={node} row={detail.data} pending={pending} />
  }

  return (
    <DetailPanel label={`Message ${id}`} name="Message" id={id}>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <EmptyState role="alert" title="This message no longer exists." />
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label="Loading the message" shape="fields" rows={4} />
      )}
    </DetailPanel>
  )
}
