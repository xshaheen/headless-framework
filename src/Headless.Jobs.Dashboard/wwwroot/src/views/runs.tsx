import { useDashboard, useModule } from '@headless/dashboard-sdk'
import {
  CommandAction,
  useAllowedActions,
  useOperations,
  type CommandDefinition,
} from '@headless/dashboard-sdk/commands'
import {
  Button,
  DetailPanel,
  EmptyState,
  FilterChip,
  FilterChips,
  FilterSelect,
  FilterToolbar,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  Skeleton,
  StatusMark,
  usePageFrame,
  Timestamp,
} from '@headless/dashboard-sdk/components'
import {
  DashboardApiError,
  isNewerVersion,
  listKey,
  useDashboardKeysetList,
  useDashboardObject,
  useNewRows,
} from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import {
  DateRangeField,
  timeRangePresets,
  useListRange,
  useTimeRangeSearch,
  type TimeRange,
} from '@headless/dashboard-sdk/date-range'
import { DataTable, SearchField, type DataTableColumn, type ListSort } from '@headless/dashboard-sdk/table'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  CellText,
  firstLine,
  JobsStats,
  QueryState,
  RunPanel,
  StaleNotice,
  useFunctions,
  useJobsAttention,
} from '../parts.tsx'
import {
  fetchRun,
  fetchRuns,
  filterNames,
  kindLabel,
  markDone,
  operationScope,
  readFilter,
  readSearch,
  readSort,
  runMatches,
  runsNoun,
  runStatusMark,
  runsTopic,
  useJobsCommands,
  usePending,
  viewOnlyReason,
  type Pending,
  type Run,
  type RunFilter,
  type RunScope,
  type RunStatus,
} from '../runs.ts'
import { progressText, RunProgress } from '../progress.tsx'
import { TimeJobForm } from './create.tsx'

const kindOptions = [
  { value: 'TimeJob', label: 'Time jobs' },
  { value: 'CronJobOccurrence', label: 'Occurrences' },
]

const states: Readonly<Record<RunStatus, { title: string; description: string }>> = {
  Idle: { title: 'Idle', description: 'Due later, waiting for a retry, or a chain step waiting for its parent.' },
  Queued: { title: 'Queued', description: 'Due now, waiting for a node to pick them up.' },
  InProgress: { title: 'In progress', description: 'Running on a node now.' },
  Succeeded: { title: 'Succeeded', description: 'Finished without an exception.' },
  DueDone: { title: 'Due done', description: 'Finished without an exception after starting later than due.' },
  Failed: { title: 'Failed', description: 'Threw on their last try; requeue them once the cause is fixed.' },
  Cancelled: { title: 'Cancelled', description: 'Stopped before they finished, by an operator or by their chain.' },
  Skipped: {
    title: 'Skipped',
    description: 'Not run, because their chain condition or overlap rule passed them over.',
  },
}

const CANCELLABLE_STATES: ReadonlySet<RunStatus | ''> = new Set(['', 'Idle', 'Queued', 'InProgress'])

type CreateKind = 'time-job' | 'chain'

export default function RunsView() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const client = useQueryClient()
  const { start } = useOperations()
  const [params, setParams] = useSearchParams()
  const filter = readFilter(params)
  // The whole view is in the URL, so a link or a reload shows the same runs. A preset range is kept by name and worked
  // out when the view opens, so a shared `Last 24 h` link stays relative. The search reads function names, ids, business
  // keys, and descriptions, never a payload, so it may sit in a link.
  const [timeRange, setTimeRange] = useTimeRangeSearch({ presets: timeRangePresets })
  const range = useListRange(timeRange)
  const search = readSearch(params)
  const sort = readSort(params)
  // Bumped to restart the search box, which keeps its own text, when the URL's search changes under it.
  const [scopeReset, setScopeReset] = useState(0)
  // The search this view last wrote, so only a search a link or Back changed restarts the box the operator types in.
  const written = useRef(search)
  const scope: RunScope = { search, range }
  const filterKey = [...filterNames.map((name) => filter[name]), search, range?.start, range?.end].join('\n')
  const creating = params.get('create')
  // The Failed list's date column shows when a run failed, so a due-time order chosen on another list does not apply.
  const listSort = filter.status === 'Failed' && sort?.key === 'executionTime' ? undefined : sort

  useEffect(() => {
    if (search !== written.current) {
      written.current = search
      setScopeReset((count) => count + 1)
    }
  }, [search])
  const runs = useDashboardKeysetList<Run>({
    topic: runsTopic,
    params: filter,
    sort: listSort,
    search,
    range,
    fetchPage: (cursor, request) => fetchRuns(api, filter, cursor, request),
  })
  const items = runs.rows
  const list = runs.query
  const matches = (run: Run) => runMatches(run, filter, scope)
  const frame = usePageFrame({ viewId: 'jobs.runs', rows: items, matches })
  const newRows = useNewRows<Run>(runsTopic, matches)
  const functions = useFunctions()
  const commands = useJobsCommands()
  const pending = usePending()
  const [allMatching, setAllMatching] = useState(false)
  // A live change can move a checked run out of the list while the cache still holds it: the bar counts and acts on
  // the checked runs the list shows, never on hidden ones.
  const checkedRuns = items.filter((run) => frame.checked.has(run.id) && matches(run))
  // The version each refused run had when it was refused; a later change to the run makes the refusal out of date.
  const refusedAt = useRef<ReadonlyMap<string, string>>(new Map())
  const status = filter.status
  const state = status ? states[status] : undefined

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

  function setFilter(name: keyof RunFilter, value: string, also?: Partial<RunFilter>) {
    update((next) => {
      for (const [key, set] of Object.entries({ [name]: value, ...also })) {
        if (set) {
          next.set(key, set)
        } else {
          next.delete(key)
        }
      }
    })
  }

  function searchFor(text: string) {
    written.current = text
    update((next) => (text ? next.set('q', text) : next.delete('q')))
  }

  function sortBy(picked: ListSort) {
    update((next) => {
      next.set('sort', picked.key)
      next.set('direction', picked.direction)
    })
  }

  // The status is the list itself, chosen in the sidebar; clearing the filters keeps it, and the sort, which narrows
  // nothing.
  function clearFilters() {
    written.current = ''
    setScopeReset((count) => count + 1)
    update((next) => {
      for (const name of [...filterNames.filter((name) => name !== 'status'), 'q', 'range', 'from', 'to']) {
        next.delete(name)
      }
    })
  }

  function openCreate(kind: CreateKind) {
    update((next) => {
      next.delete('open')
      next.set('create', kind)
    })
  }

  // A row opened while the form is up replaces it, so closing the run does not bring the form back.
  useEffect(() => {
    if (frame.openId && creating) {
      update((next) => next.delete('create'))
    }
  }, [frame.openId])

  // The palette's `Requeue failed runs` opens this view with `operation=requeue`; the view starts the Operation once.
  const started = useRef(false)
  const wantsOperation = params.get('operation') === 'requeue'
  const requeue = commands.requeue

  useEffect(() => {
    if (!wantsOperation || !requeue || started.current) {
      return
    }

    started.current = true
    update((next) => next.delete('operation'))

    if (requeue.canRun) {
      start({
        module: module.id,
        command: requeue,
        filter: operationScope({ ...filter, status: 'Failed' }, scope),
        noun: runsNoun({ ...filter, status: 'Failed' }),
      })
    }
    // Runs once per palette request; the filter is read as it is now.
  }, [wantsOperation, requeue])

  useEffect(() => {
    if (!wantsOperation) {
      started.current = false
    }
  }, [wantsOperation])

  // A new filter is a new list, whether the header, the sidebar, a link, or Back changed it: the checked rows, and so
  // `Select all matching` and the refusals, answered for the old one.
  useEffect(() => frame.setChecked(new Set()), [filterKey])

  // `Select all matching` answers for the rows checked when it was pressed; a change to them ends it.
  useEffect(() => setAllMatching(false), [frame.checked])

  // A checked run a live change moved out of the list leaves the selection, so it does not come back checked; while
  // every matching run is selected the Operation scopes by the filter, so the checked rows stay. A refused run that
  // changed since is answered afresh by the next action, so its tag goes.
  useEffect(() => {
    const left = allMatching ? [] : items.filter((run) => frame.checked.has(run.id) && !matches(run))

    if (left.length > 0) {
      const next = new Set(frame.checked)
      left.forEach((run) => next.delete(run.id))
      frame.setChecked(next)

      return
    }

    const changed = new Set(
      items
        .filter((run) => frame.refused.has(run.id) && isNewerVersion(run.version, refusedAt.current.get(run.id)))
        .map((run) => run.id),
    )

    if (changed.size > 0) {
      frame.setRefused(new Map([...frame.refused].filter(([id]) => !changed.has(id))))
    }
  }, [items])

  const count = runs.count
  const narrowed =
    filterNames.some((name) => name !== 'status' && filter[name] !== '') || search !== '' || timeRange !== undefined
  const addReason = module.canOperate ? null : viewOnlyReason
  const functionOptions = [
    ...new Set([...(functions.data ?? []).map((item) => item.name), ...(filter.function ? [filter.function] : [])]),
  ].map((name) => ({ value: name, label: name }))
  const openRun = items.find((run) => run.id === frame.openId)
  // The filters a link set that the header shows as chips to clear: on a state list, every one but the status; on All
  // runs, those with no select. On the Failed list a pressed cause chip already shows its fingerprint and function.
  const chipped: readonly { name: keyof RunFilter; label: string }[] = [
    ...(status ? [{ name: 'kind', label: 'kind' } as const, { name: 'function', label: 'function' } as const] : []),
    { name: 'correlationId', label: 'correlation' },
    { name: 'fingerprint', label: 'cause' },
    { name: 'cronJobId', label: 'cron job' },
  ]
  const setFilters = chipped.filter(
    ({ name }) =>
      filter[name] && !(status === 'Failed' && filter.fingerprint && (name === 'fingerprint' || name === 'function')),
  )

  let panel
  if (frame.openId) {
    panel = openRun ? <RunPanel run={openRun} pending={pending} /> : <LookedUpRun id={frame.openId} pending={pending} />
  } else if (creating === 'time-job' || creating === 'chain') {
    panel = (
      <TimeJobForm
        chain={creating === 'chain'}
        onChainChange={(chain) => update((next) => next.set('create', chain ? 'chain' : 'time-job'))}
        onCancel={() => update((next) => next.delete('create'))}
        onCreated={(id) => {
          void client.invalidateQueries({ queryKey: listKey(runsTopic) })
          update((next) => {
            next.delete('create')
            next.set('open', id)
          })
        }}
      />
    )
  }

  return (
    <PageFrame
      // The form is not a row, so the panel's Close ends the form rather than closing an open row.
      frame={
        panel !== undefined && !frame.openId
          ? { ...frame, close: () => update((next) => next.delete('create')) }
          : frame
      }
      form={panel !== undefined && !frame.openId}
      stats={<JobsStats />}
      header={
        <PageHeader
          title={state?.title ?? 'All runs'}
          tone={status ? runStatusMark({ status }).tone : undefined}
          count={count ? format.count(count) : undefined}
          description={state?.description ?? 'Every time job and cron occurrence, newest first.'}
          toolbar={
            <FilterToolbar
              active={[filter.kind, filter.function, timeRange].filter(Boolean).length}
              chips={
                <>
                  {status === 'Failed' ? (
                    <FailureChips filter={filter} count={count ? format.count(count) : undefined} onPick={setFilter} />
                  ) : null}
                  {setFilters.length > 0 || (status && timeRange) ? (
                    <FilterChips label="Set filters">
                      {setFilters.map(({ name, label }) => (
                        <FilterChip key={name} label={label} value={filter[name]} onClear={() => setFilter(name, '')} />
                      ))}
                      {/* A state list has no range control, so a range a link set shows here to clear. */}
                      {status && timeRange ? (
                        <FilterChip label="due" value={rangeText(timeRange)} onClear={() => setTimeRange(undefined)} />
                      ) : null}
                    </FilterChips>
                  ) : null}
                </>
              }
              search={
                // Restarted with the URL's text when a link, Back, or Clear filters changes it.
                <SearchField
                  key={`search-${scopeReset}`}
                  label="Search runs"
                  placeholder="Function, id, key, description"
                  compact
                  defaultValue={search}
                  onSearch={searchFor}
                />
              }
            >
              {/* A state list keeps only the search, as the sidebar already chose the state: its other filters
                  arrive by link and show as chips to clear. The All runs list offers every filter. */}
              {status ? null : (
                <>
                  <FilterSelect
                    label="Kind"
                    emptyLabel="Any kind"
                    value={filter.kind}
                    onChange={(value) => setFilter('kind', value)}
                    options={kindOptions}
                  />
                  <FilterSelect
                    label="Function"
                    emptyLabel="Any function"
                    value={filter.function}
                    onChange={(value) => setFilter('function', value)}
                    options={functionOptions}
                  />
                  {/* The server filters every list's range by when each run is due. */}
                  <DateRangeField
                    label="Due"
                    value={timeRange}
                    onChange={setTimeRange}
                    presets={timeRangePresets}
                    compact
                  />
                </>
              )}
            </FilterToolbar>
          }
          actions={
            <>
              {allMatching && count ? (
                <MatchingActions
                  filter={filter}
                  scope={scope}
                  count={format.count(count)}
                  cancel={CANCELLABLE_STATES.has(status)}
                />
              ) : checkedRuns.length > 0 ? (
                <CheckedActions
                  runs={checkedRuns}
                  pending={pending}
                  cancel={CANCELLABLE_STATES.has(status)}
                  frame={frame}
                  onRefused={(reasons, versions) => {
                    refusedAt.current = versions
                    frame.setRefused(reasons)
                  }}
                />
              ) : null}
              <Button variant="primary" disabledReason={addReason} onPress={() => openCreate('time-job')}>
                Add time job
              </Button>
              <Button disabledReason={addReason} onPress={() => openCreate('chain')}>
                Add chain
              </Button>
            </>
          }
        />
      }
      panel={panel}
    >
      <StaleNotice what="these runs" onRefresh={() => void list.refetch()} />
      <QueryState query={list} label="Loading runs" rows={6} shape="table">
        {() => (
          <RunsTable
            // A new filter is a new list: the held rows and the scroll start again.
            key={filterKey}
            frame={frame}
            runs={runs}
            matches={matches}
            newRows={newRows}
            failed={status === 'Failed'}
            pending={pending}
            sort={listSort}
            onSortChange={sortBy}
            onSelectAllMatching={() => setAllMatching(true)}
            emptyState={
              narrowed ? (
                <span>
                  No runs match. Clear filters or widen the time range.{' '}
                  <Button variant="ghost" onPress={clearFilters}>
                    Clear filters
                  </Button>
                </span>
              ) : state ? (
                `No ${state.title.toLowerCase()} runs.`
              ) : (
                'No runs yet. A run appears when a host schedules a time job or a cron job fires; add a time job here.'
              )
            }
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

/** A range as a chip reads it: the preset's name, such as `24h`, or its first and last days. */
function rangeText(range: TimeRange) {
  return range.kind === 'preset' ? range.preset : `${range.start} – ${range.end}`
}

interface RunsTableProps {
  frame: ReturnType<typeof usePageFrame>
  runs: ReturnType<typeof useDashboardKeysetList<Run>>
  matches: (run: Run) => boolean
  newRows: ReturnType<typeof useNewRows<Run>>
  failed: boolean
  pending: Pending
  sort: ListSort | undefined
  onSortChange: (sort: ListSort) => void
  onSelectAllMatching: () => void
  emptyState: ReactNode
}

function RunsTable({
  frame,
  runs,
  matches,
  newRows,
  failed,
  pending,
  sort,
  onSortChange,
  onSelectAllMatching,
  emptyState,
}: RunsTableProps) {
  const format = useFormat()
  // The Failed list says why each run failed; the server serves the message, not the exception's type.
  const reasonColumn: DataTableColumn<Run> = {
    id: 'reason',
    header: 'Reason',
    cell: (run) => {
      const reason = firstLine(run.exceptionMessage)

      return <CellText title={reason}>{reason}</CellText>
    },
    priority: 2,
    width: '3fr',
  }

  return (
    <DataTable
      label="Runs"
      viewId="jobs.runs"
      frame={frame}
      rows={runs.rows}
      matches={matches}
      hasMore={runs.hasMore}
      isLoadingMore={runs.isLoadingMore}
      onLoadMore={runs.loadMore}
      newRows={newRows}
      selectionMode="multiple"
      matchingCount={runs.count?.value}
      sort={sort}
      onSortChange={onSortChange}
      onSelectAllMatching={onSelectAllMatching}
      emptyState={emptyState}
      columns={[
        {
          id: 'function',
          header: 'Function',
          cell: (run) => (
            <CellText title={run.function}>
              <Mono>{run.function}</Mono>
            </CellText>
          ),
          isRowHeader: true,
          kind: 'id',
          width: '2fr',
          sortKey: 'function',
        },
        {
          id: 'status',
          header: 'Status',
          cell: (run) => (
            <CellText>
              <StatusMark {...runStatusMark(run, pending.labelOf(run))} />
            </CellText>
          ),
          kind: 'status',
          width: 130,
          sortKey: 'status',
        },
        ...(failed ? [reasonColumn] : []),
        {
          id: 'progress',
          header: 'Progress',
          cell: (run) =>
            run.progressPercent !== undefined ? (
              <CellText
                title={progressText(run, `${format.number(run.progressPercent, { maximumFractionDigits: 1 })} %`)}
              >
                <RunProgress run={run} compact />
              </CellText>
            ) : (
              ''
            ),
          priority: 2,
          width: 240,
        },
        { id: 'kind', header: 'Kind', cell: (run) => kindLabel(run.kind), priority: 2, width: 110 },
        {
          id: 'retries',
          header: 'Retries',
          cell: (run) => format.number(run.retryCount),
          kind: 'number',
          priority: 2,
          width: 80,
        },
        {
          id: 'duration',
          header: 'Duration',
          cell: (run) => (run.executedAt ? format.duration(run.elapsedMilliseconds) : ''),
          kind: 'duration',
          priority: 2,
          width: 100,
        },
        {
          id: 'due',
          header: failed ? 'Failed at' : 'Due',
          cell: (run) => (
            <CellText>
              <Timestamp
                value={failed ? (run.executedAt ?? run.executionTime) : (run.executionTime ?? run.createdAt)}
              />
            </CellText>
          ),
          kind: 'time',
          width: 110,
          // The Failed list shows when each run failed, while the server sorts by when it was due.
          ...(failed ? {} : { sortKey: 'executionTime' }),
        },
      ]}
    />
  )
}

/**
 * The Failed list's causes: `All`, then one chip per failure group the server counts, labelled by its function; the
 * server does not serve a group's exception type, and the chip's title names its fingerprint.
 */
function FailureChips({
  filter,
  count,
  onPick,
}: {
  filter: RunFilter
  count?: string
  onPick: (name: keyof RunFilter, value: string, also?: Partial<RunFilter>) => void
}) {
  const format = useFormat()
  const groups = useJobsAttention().items.flatMap((item) => {
    const scope = item.recommended?.filter
    const fn = scope?.functions?.[0]

    // A group stored without a fingerprint has no filter that selects only its runs.
    return fn && scope.fingerprint
      ? [{ id: item.id, fn, fingerprint: scope.fingerprint, count: { value: item.count, capped: item.capped } }]
      : []
  })

  return (
    <FilterChips label="Filter by cause">
      <FilterChip
        label="All"
        count={count}
        pressed={!filter.fingerprint}
        onPress={() => onPick('fingerprint', '', { function: '' })}
      />
      {groups.map((group) => {
        // Two functions can fail the same way, so a chip is its pair: one function and one fingerprint.
        const pressed = filter.fingerprint === group.fingerprint && filter.function === group.fn

        return (
          <FilterChip
            key={group.id}
            label={group.fn}
            title={`${group.fn}, fingerprint ${group.fingerprint}`}
            count={format.count(group.count)}
            failing
            pressed={pressed}
            onPress={() =>
              onPick('fingerprint', pressed ? '' : group.fingerprint, { function: pressed ? '' : group.fn })
            }
          />
        )
      })}
    </FilterChips>
  )
}

function CheckedActions({
  runs,
  pending,
  cancel,
  frame,
  onRefused,
}: {
  runs: readonly Run[]
  pending: Pending
  cancel: boolean
  frame: ReturnType<typeof usePageFrame>
  onRefused: (reasons: ReadonlyMap<string, string>, versions: ReadonlyMap<string, string>) => void
}) {
  const module = useModule()
  const commands = useJobsCommands()
  const allowed = useAllowedActions('run', runs)
  const ids = runs.map((run) => run.id)
  // Captured at render, so the versions are the ones the operator saw when they ran the command.
  const versions = new Map(runs.map((run) => [run.id, run.version]))

  const action = (
    name: 'requeue' | 'cancel' | 'delete',
    command: CommandDefinition | undefined,
    variant?: 'primary' | 'danger',
  ) =>
    command ? (
      <CommandAction
        key={name}
        module={module.id}
        command={command}
        ids={ids}
        noun={ids.length === 1 ? 'run' : 'runs'}
        variant={variant}
        commands={commands.all}
        refused={allowed.refused(command.id)}
        onRefused={(reasons) => onRefused(reasons, versions)}
        onViewRefused={frame.reveal}
        onDone={markDone(pending, versions, name)}
      />
    ) : null

  return (
    <>
      <span>{`${runs.length} selected`}</span>
      {action('requeue', commands.requeue, 'primary')}
      {cancel ? action('cancel', commands.cancel) : null}
      {action('delete', commands.delete, 'danger')}
    </>
  )
}

/** Every run the filter matches, past the loaded page: each action runs over them as an Operation. */
function MatchingActions({
  filter,
  scope,
  count,
  cancel,
}: {
  filter: RunFilter
  scope: RunScope
  count: string
  cancel: boolean
}) {
  const module = useModule()
  const { start } = useOperations()
  const commands = useJobsCommands()
  const noun = runsNoun(filter)

  const operation = (command: CommandDefinition | undefined, variant?: 'primary' | 'danger') =>
    command ? (
      <Button
        key={command.id}
        variant={variant}
        disabledReason={command.canRun ? null : (command.disabledReason ?? viewOnlyReason)}
        onPress={() => start({ module: module.id, command, filter: operationScope(filter, scope), noun })}
      >
        {`${command.label} ${count}`}
      </Button>
    ) : null

  return (
    <>
      <span>{`All ${count} matching ${noun}`}</span>
      {operation(commands.requeue, 'primary')}
      {cancel ? operation(commands.cancel) : null}
      {operation(commands.delete, 'danger')}
    </>
  )
}

function LookedUpRun({ id, pending }: { id: string; pending: Pending }) {
  const { api } = useDashboard()
  const detail = useDashboardObject({ topic: runsTopic, id, fetch: () => fetchRun(api, id) })

  if (detail.data) {
    return <RunPanel run={detail.data} pending={pending} />
  }

  return (
    <DetailPanel label={`Run ${id}`} name="Run" id={id}>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <EmptyState role="alert" title="This run no longer exists." />
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label="Loading the run" shape="fields" rows={4} />
      )}
    </DetailPanel>
  )
}
