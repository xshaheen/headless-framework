import { useDashboard, type DashboardStateRole } from '@headless/dashboard-sdk'
import {
  Banner,
  Button,
  Callout,
  DetailPanel,
  EmptyState,
  FieldGrid,
  FieldGroup,
  Fields,
  FilterChip,
  FilterChips,
  FilterToolbar,
  GridField,
  KeyValueTable,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  SegmentedControl,
  StatCard,
  StatStrip,
  Tag,
  Text,
  Timestamp,
  usePageFrame,
  type PageFrameState,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardKeysetList } from '@headless/dashboard-sdk/data'
import { ListIcon } from '@headless/dashboard-sdk/icons'
import { isTimeZone, useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable, SearchField } from '@headless/dashboard-sdk/table'
import { useCallback, useEffect, useState, type ReactNode } from 'react'
import {
  countersTopic,
  fetchCounters,
  fetchPartitions,
  isReadable,
  modeLabels,
  partitionsTopic,
  readFilter,
  refreshSequences,
  resetLabels,
  sequenceModes,
  useCounter,
  useSummary,
  type CounterFilter,
  type SequenceCounter,
  type SequenceMode,
  type SequencesSummary,
} from '../api.ts'

type ScopeChoice = 'all' | 'host' | 'tenant'

const scopeOptions = [
  { value: 'all', label: 'All' },
  { value: 'host', label: 'Host' },
  { value: 'tenant', label: 'Tenant' },
] as const satisfies readonly { value: ScopeChoice; label: string }[]

type PartitionChoice = 'all' | 'current'

const partitionOptions = [
  { value: 'all', label: 'All partitions' },
  { value: 'current', label: 'Current only' },
] as const satisfies readonly { value: PartitionChoice; label: string }[]

/** The list as the page holds it: read, or not read because the tenant scope names no tenant yet. */
type CounterList = { kind: 'read'; list: ReturnType<typeof useCounterList> } | { kind: 'waiting' }

export default function SequencesView() {
  const [params] = useSearchParams()
  const filter = readFilter(params)

  // A tenant scope with no id reads nothing, so the list is never mounted for it and no request goes out.
  return isReadable(filter) ? <ReadPage filter={filter} /> : <Page filter={filter} counters={{ kind: 'waiting' }} />
}

function ReadPage({ filter }: { filter: CounterFilter }) {
  const list = useCounterList(filter)

  return <Page filter={filter} counters={{ kind: 'read', list }} />
}

function useCounterList(filter: CounterFilter) {
  const { api } = useDashboard()

  return useDashboardKeysetList<SequenceCounter>({
    topic: countersTopic,
    params: filter,
    fetchPage: (cursor) => fetchCounters(api, filter, cursor),
  })
}

function Page({ filter, counters }: { filter: CounterFilter; counters: CounterList }) {
  const format = useFormat()
  const [, setParams] = useSearchParams()
  const summary = useSummary(filter)
  const rows = counters.kind === 'read' ? counters.list.rows : []
  const frame = usePageFrame({ viewId: 'sequences.counters', rows })
  // Choosing Tenant keeps the list until an id is submitted; only a submitted id enters the URL and the requests.
  const [isChoosingTenant, setChoosingTenant] = useState(false)
  const scopeChoice: ScopeChoice = isChoosingTenant ? 'tenant' : (filter.scope ?? 'all')
  const active = [filter.mode, filter.scope, filter.current, filter.q].filter(Boolean).length

  function update(change: (next: URLSearchParams) => void) {
    setParams((current) => {
      const next = new URLSearchParams(current)
      change(next)

      return next
    })
  }

  function chooseScope(choice: ScopeChoice) {
    if (choice === 'tenant') {
      setChoosingTenant(true)

      return
    }

    setChoosingTenant(false)
    update((next) => {
      next.delete('tenant')

      if (choice === 'host') {
        next.set('scope', 'host')
      } else {
        next.delete('scope')
      }
    })
  }

  function submitTenant(tenant: string) {
    setChoosingTenant(false)
    update((next) => {
      next.set('scope', 'tenant')

      if (tenant) {
        next.set('tenant', tenant)
      } else {
        next.delete('tenant')
      }
    })
  }

  function setMode(mode: SequenceMode | undefined) {
    update((next) => (mode ? next.set('mode', mode) : next.delete('mode')))
  }

  function clearFilters() {
    setChoosingTenant(false)
    update((next) => {
      for (const name of ['mode', 'scope', 'tenant', 'current', 'q']) {
        next.delete(name)
      }
    })
  }

  const list = counters.kind === 'read' ? counters.list : undefined
  const listed = list?.rows.find((row) => row.id === frame.openId)
  const client = useQueryClient()
  const isReadableFilter = isReadable(filter)

  // A tenant scope with no id names nothing to read, so neither Refresh nor focus sends a request for it, an open
  // panel's reads included.
  const refresh = useCallback(() => {
    if (isReadableFilter) {
      refreshSequences(client)
    }
  }, [client, isReadableFilter])

  // The inspector serves no change feed, so the view reads again when the operator comes back to the window.
  useEffect(() => {
    if (!isReadableFilter) {
      return
    }

    window.addEventListener('focus', refresh)

    return () => window.removeEventListener('focus', refresh)
  }, [refresh, isReadableFilter])

  return (
    <PageFrame
      frame={frame}
      stats={<SummaryStats summary={summary.data} filter={filter} />}
      header={
        <PageHeader
          title="Sequences"
          count={
            summary.data
              ? `${format.count({ value: summary.data.total, capped: summary.data.capped })} counters`
              : undefined
          }
          description={<ReadAt list={list} />}
          actions={<Button onPress={refresh}>Refresh</Button>}
          toolbar={
            <FilterToolbar
              active={active}
              chips={<ModeChips summary={summary.data} mode={filter.mode} onPick={setMode} />}
              search={
                // Restarted with the URL's text when Clear filters or Back changes it.
                <SearchField
                  key={`search-${filter.q ?? ''}`}
                  label="Search counters"
                  placeholder="Name or tenant id"
                  compact
                  defaultValue={filter.q}
                  onSearch={(text) => update((next) => (text ? next.set('q', text) : next.delete('q')))}
                />
              }
            >
              <SegmentedControl label="Scope" options={scopeOptions} value={scopeChoice} onChange={chooseScope} />
              {scopeChoice === 'tenant' ? (
                <SearchField
                  key={`tenant-${filter.tenant ?? ''}`}
                  label="Tenant id"
                  placeholder="Exact tenant id"
                  mode="submit"
                  compact
                  defaultValue={filter.tenant}
                  onSearch={submitTenant}
                />
              ) : null}
              <SegmentedControl
                label="Partitions"
                options={partitionOptions}
                value={filter.current ? 'current' : 'all'}
                onChange={(choice) =>
                  update((next) => (choice === 'current' ? next.set('current', 'true') : next.delete('current')))
                }
              />
            </FilterToolbar>
          }
        />
      }
      panel={frame.openId ? <CounterPanel id={frame.openId} listed={listed} /> : undefined}
    >
      {summary.isError && !list?.query.isError ? (
        <LoadError
          message={`The counts did not load: ${summary.error.message}`}
          kept={summary.data !== undefined}
          onRetry={() => void summary.refetch()}
        />
      ) : null}
      {list ? (
        <QueryState query={list.query} label="Loading counters" rows={4} shape="table">
          {() => <CountersTable list={list} frame={frame} isFiltered={active > 0} onClear={clearFilters} />}
        </QueryState>
      ) : (
        <EmptyState icon={ListIcon} title="Enter a tenant id" placement="region">
          Type an exact tenant id in the Tenant id field and press Enter to list that tenant&apos;s counters.
        </EmptyState>
      )}
    </PageFrame>
  )
}

/** When the list was last read, since the inspector serves no changes: the view refreshes on Refresh and on focus. */
function ReadAt({ list }: { list: ReturnType<typeof useCounterList> | undefined }) {
  const updatedAt = list?.query.dataUpdatedAt

  return (
    <>
      View only. Reading a counter never issues a number.
      {updatedAt ? (
        <>
          {' Read '}
          <Timestamp value={new Date(updatedAt).toISOString()} />
        </>
      ) : null}
    </>
  )
}

/**
 * The list a mode card counted: the summary reads the scope, tenant, and search and ignores the current filter, so the
 * link keeps those three, sets the mode, and leaves `current` and an open panel behind.
 */
function modeLink(filter: CounterFilter, mode: SequenceMode) {
  const next = new URLSearchParams()

  if (filter.scope) {
    next.set('scope', filter.scope)
  }

  if (filter.tenant) {
    next.set('tenant', filter.tenant)
  }

  if (filter.q) {
    next.set('q', filter.q)
  }

  next.set('mode', mode)

  return `?${next.toString()}`
}

function SummaryStats({ summary, filter }: { summary: SequencesSummary | undefined; filter: CounterFilter }) {
  const format = useFormat()

  function value(count: number | undefined, capped = summary?.capped ?? false) {
    return count === undefined ? '–' : format.count({ value: count, capped })
  }

  return (
    <StatStrip label="Sequences at a glance">
      <StatCard label="Counters" value={value(summary?.total)} />
      <StatCard label="Current partitions" value={value(summary?.current, summary?.currentCapped)} />
      <StatCard label="Gap-free" value={value(summary?.gapFree)} to={modeLink(filter, 'GapFree')} />
      <StatCard label="Fast" value={value(summary?.fast)} to={modeLink(filter, 'Fast')} />
      <StatCard label="Reported" value={value(summary?.reported)} to={modeLink(filter, 'Reported')} />
      <StatCard
        label="Next reset"
        value={summary ? <Timestamp value={summary.nextResetAt} scheduled missing="None" /> : '–'}
        caption="Earliest across every policy"
      />
    </StatStrip>
  )
}

/** All and one chip per mode; the summary ignores the mode filter, so every chip keeps its count while one is on. */
function ModeChips({
  summary,
  mode,
  onPick,
}: {
  summary: SequencesSummary | undefined
  mode: SequenceMode | undefined
  onPick: (mode: SequenceMode | undefined) => void
}) {
  const format = useFormat()
  const counts: Readonly<Record<SequenceMode, number>> | undefined = summary && {
    Fast: summary.fast,
    GapFree: summary.gapFree,
    Reported: summary.reported,
  }

  function shown(value: number | undefined) {
    return value === undefined ? undefined : format.count({ value, capped: summary?.capped ?? false })
  }

  return (
    <FilterChips label="Filter by mode">
      <FilterChip
        label="All"
        count={shown(summary?.total)}
        pressed={mode === undefined}
        onPress={() => onPick(undefined)}
      />
      {sequenceModes.map((candidate) => (
        <FilterChip
          key={candidate}
          label={modeLabels[candidate]}
          count={shown(counts?.[candidate])}
          pressed={mode === candidate}
          onPress={() => onPick(mode === candidate ? undefined : candidate)}
        />
      ))}
    </FilterChips>
  )
}

function CountersTable({
  list,
  frame,
  isFiltered,
  onClear,
}: {
  list: ReturnType<typeof useCounterList>
  frame: PageFrameState
  isFiltered: boolean
  onClear: () => void
}) {
  const format = useFormat()

  return (
    <DataTable
      label="Counters"
      frame={frame}
      rows={list.rows}
      hasMore={list.hasMore}
      isLoadingMore={list.isLoadingMore}
      onLoadMore={list.loadMore}
      emptyIcon={ListIcon}
      emptyTitle={isFiltered ? 'No counters match' : 'No counters yet'}
      emptyState={
        isFiltered
          ? 'No stored counter matches these filters.'
          : 'A counter is stored when the app takes its first number. Register numbering with services.AddHeadlessSequences(...), then issue a number.'
      }
      emptyAction={isFiltered ? <Button onPress={onClear}>Clear filters</Button> : undefined}
      columns={[
        {
          id: 'tenant',
          header: 'tenant',
          cell: (row) => <TenantText counter={row} />,
          width: '1fr',
        },
        {
          id: 'name',
          header: 'name',
          cell: (row) => <Mono>{row.name}</Mono>,
          isRowHeader: true,
          kind: 'id',
          width: '1.5fr',
        },
        {
          id: 'mode',
          header: 'mode',
          cell: (row) => <ModeText counter={row} />,
          width: 160,
        },
        {
          id: 'reset',
          header: 'reset',
          cell: (row) => resetLabels[row.reset],
          priority: 2,
          width: 100,
        },
        {
          id: 'partition',
          header: 'partition',
          cell: (row) => <PartitionText counter={row} />,
          width: 150,
        },
        {
          id: 'value',
          header: 'value',
          cell: (row) => format.number(row.value),
          kind: 'number',
          width: 100,
        },
        {
          id: 'lastNumber',
          header: 'last issued',
          cell: (row) => <LastNumber counter={row} />,
          priority: 2,
          width: '1.5fr',
        },
        {
          id: 'nextReset',
          header: 'next reset',
          cell: (row) => <Timestamp value={row.nextResetAt} scheduled missing="Never" />,
          kind: 'time',
          priority: 2,
          width: 140,
        },
        {
          id: 'updated',
          header: 'updated',
          cell: (row) => <Timestamp value={row.updatedAt} />,
          kind: 'time',
          width: 140,
        },
      ]}
    />
  )
}

function TenantText({ counter }: { counter: SequenceCounter }) {
  return counter.isHost ? 'Host' : <Mono>{counter.tenantId}</Mono>
}

function ModeText({ counter }: { counter: SequenceCounter }) {
  return (
    <>
      {modeLabels[counter.mode]}
      {counter.policySource === 'default' ? (
        <>
          {' '}
          <Tag title="No policy is registered under this name, so it numbers with the default policy">
            Default policy
          </Tag>
        </>
      ) : null}
    </>
  )
}

function PartitionText({ counter }: { counter: SequenceCounter }) {
  return (
    <>
      {counter.partition ? <Mono>{counter.partition}</Mono> : 'None'}
      {counter.isCurrentPartition ? null : (
        <>
          {' '}
          <Tag>Closed</Tag>
        </>
      )}
    </>
  )
}

function LastNumber({ counter }: { counter: SequenceCounter }) {
  if (counter.lastNumber !== null) {
    return <Mono>{counter.lastNumber}</Mono>
  }

  return counter.mode === 'Reported' ? 'Not issued here' : 'No format'
}

function partitionMark(counter: SequenceCounter): { tone: DashboardStateRole; label: string } {
  return counter.isCurrentPartition ? { tone: 'ok', label: 'Current' } : { tone: 'off', label: 'Closed' }
}

function scopeName(counter: SequenceCounter) {
  return counter.isHost ? 'Host' : counter.tenantId
}

/**
 * The open counter. A row the list holds opens from that row; only a panel opened from a URL whose row is not listed
 * reads the counter by id.
 */
function CounterPanel({ id, listed }: { id: string; listed: SequenceCounter | undefined }) {
  const read = useCounter(listed ? undefined : id)

  if (listed) {
    return <CounterDetail counter={listed} />
  }

  if (read.isError && read.error instanceof DashboardApiError && read.error.status === 404) {
    return (
      <DetailPanel label="Sequence counter" name={id}>
        <Banner tone="warn">{read.error.message}</Banner>
      </DetailPanel>
    )
  }

  // A failed refresh keeps the last good read on screen under the error, so the panel never passes stale data as fresh.
  return read.data ? (
    <CounterDetail
      counter={read.data}
      notice={
        read.isError ? <LoadError message={read.error.message} kept onRetry={() => void read.refetch()} /> : undefined
      }
    />
  ) : (
    <DetailPanel label="Sequence counter" name={id}>
      <QueryState query={read} label="Loading the counter" rows={6} shape="fields">
        {() => null}
      </QueryState>
    </DetailPanel>
  )
}

function CounterDetail({ counter, notice }: { counter: SequenceCounter; notice?: ReactNode }) {
  return (
    <DetailPanel
      label={`Sequence counter ${counter.name}`}
      name={counter.name}
      id={counter.id}
      status={partitionMark(counter)}
    >
      {/* The panel body gives each direct child its own column that scrolls by itself, so everything but the
          partitions shares the first column: a third child would split the body into rows of clipped boxes. */}
      <FieldGroup>
        {notice}
        <CounterFields counter={counter} />
        <PolicyFields counter={counter} />
        <Callout title="Read-only.">
          Editing a counter risks duplicate document numbers; reading never issues a number.
        </Callout>
      </FieldGroup>
      <Partitions counter={counter} />
    </DetailPanel>
  )
}

function CounterFields({ counter }: { counter: SequenceCounter }) {
  const format = useFormat()
  const { policy } = counter
  // A policy may name a Windows zone id, which the browser's Intl API does not know: the exact time then reads in the
  // display zone, and the configured id stays visible in the policy's Time zone field.
  const zone = isTimeZone(policy.timeZone) ? policy.timeZone : undefined

  return (
    <>
      <FieldGrid label="Counter">
        <GridField label="Scope">
          <TenantText counter={counter} />
        </GridField>
        <GridField label="Partition">{counter.partition ? <Mono>{counter.partition}</Mono> : 'None'}</GridField>
        <GridField label="Value">{format.number(counter.value)}</GridField>
        <GridField label="Last issued">
          <LastNumber counter={counter} />
        </GridField>
        <GridField label="Last update">
          <Timestamp value={counter.updatedAt} />
        </GridField>
        <GridField label="Next reset">
          <Timestamp value={counter.nextResetAt} zone={zone} scheduled missing="Never" />
        </GridField>
      </FieldGrid>
      {policy.blockSize > 1 ? (
        <Text as="p" tone="hint">
          {`This name reserves ${format.number(policy.blockSize)} values at a time, so the value is the end of the last reserved block, not the last number handed out.`}
        </Text>
      ) : null}
    </>
  )
}

function PolicyFields({ counter }: { counter: SequenceCounter }) {
  const format = useFormat()
  const { policy } = counter

  return (
    <FieldGrid
      label="Policy"
      footnote={counter.policySource === 'default' ? 'Default policy' : 'Registered for this name'}
    >
      <GridField label="Start">{format.number(policy.start)}</GridField>
      <GridField label="Step">{format.number(policy.step)}</GridField>
      <GridField label="Mode">{modeLabels[policy.mode]}</GridField>
      <GridField label="Block size">{format.number(policy.blockSize)}</GridField>
      <GridField label="Reset">{resetLabels[policy.reset]}</GridField>
      <GridField label="Format">{policy.format ? <Mono>{policy.format}</Mono> : 'None'}</GridField>
      <GridField label="Fiscal-year start month">{format.number(policy.fiscalYearStartMonth)}</GridField>
      <GridField label="Time zone">
        <Mono>{policy.timeZone}</Mono>
      </GridField>
    </FieldGrid>
  )
}

function Partitions({ counter }: { counter: SequenceCounter }) {
  const { api } = useDashboard()
  const format = useFormat()
  const partitions = useDashboardKeysetList<SequenceCounter>({
    topic: partitionsTopic,
    params: { id: counter.id },
    fetchPage: (cursor) => fetchPartitions(api, counter.id, cursor),
  })

  return (
    // A page `Section` pads its heading to the page gutter, which inside the panel's padded column sets it in from the
    // rows it heads; the panel's own heading style lines up with them.
    <Fields label="Partitions">
      <Text as="h3" tone="label">
        Partitions
      </Text>
      <Text as="p" tone="hint">{`Every partition of ${counter.name} in ${scopeName(counter)}, newest first.`}</Text>
      <QueryState query={partitions.query} label="Loading the partitions" rows={3}>
        {() => (
          <>
            <KeyValueTable
              label="Partitions"
              rows={partitions.rows.map((row) => ({
                name: row.partition || 'None',
                value: `${format.number(row.value)} · ${partitionMark(row).label}`,
              }))}
            />
            {partitions.hasMore ? (
              <Button isDisabled={partitions.isLoadingMore} onPress={partitions.loadMore}>
                Load more partitions
              </Button>
            ) : null}
          </>
        )}
      </QueryState>
    </Fields>
  )
}
