import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { useOperations, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import {
  Button,
  FilterChip,
  Inspector,
  Mono,
  ObjectHeader,
  SelectField,
  StatusMark,
  TenantChip,
  useDelayed,
} from '@headless/dashboard-sdk/components'
import { DataTable } from '@headless/dashboard-sdk/table'
import { DashboardApiError, listKey, useDashboardKeysetList, useDashboardObject, useNewRows } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery, useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { useEffect, useRef, useState } from 'react'
import { fetchFunctions } from '../cron.ts'
import {
  AttemptTimeline,
  canvas,
  CellText,
  layout,
  LoadError,
  RunActions,
  RunFields,
  RunPayload,
  Skeleton,
  StaleNotice,
  Time,
  useTraceKey,
} from '../parts.tsx'
import {
  fetchRun,
  fetchRuns,
  filterNames,
  formatDuration,
  isFiltered,
  kindLabel,
  operationScope,
  rangeSpans,
  readFilter,
  runMatches,
  runsNoun,
  runStatuses,
  runStatusMark,
  runsTopic,
  statusLabel,
  useJobsCommands,
  usePending,
  viewOnlyReason,
  type Pending,
  type Run,
  type RunFilter,
} from '../runs.ts'
import { TimeJobForm } from './create.tsx'

const kindOptions = [
  { value: 'TimeJob', label: 'Time jobs' },
  { value: 'CronJobOccurrence', label: 'Occurrences' },
]
const statusOptions = runStatuses.map((status) => ({ value: status, label: statusLabel(status) }))
const allLoaded = '*'
const rangeOptions = [
  { value: '1h', label: 'Last hour' },
  { value: '24h', label: 'Last 24 h' },
  { value: '7d', label: 'Last 7 days' },
]

/**
 * Runs: one live, keyset-paged list of time jobs and cron occurrences, filtered in the URL, with the selection's
 * inspector, Operations over every matching run, and the time job and chain forms.
 */
export default function RunsView() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const client = useQueryClient()
  const { start } = useOperations()
  const [params, setParams] = useSearchParams()
  const filter = readFilter(params)
  const filterKey = filterNames.map((name) => filter[name]).join('\n')
  const creating = params.get('create')
  const runs = useDashboardKeysetList<Run>({ topic: runsTopic, params: filter, fetchPage: (cursor) => fetchRuns(api, filter, cursor) })
  const items = runs.rows
  // `selected=*` is every loaded run (`⌘A`): one marker, not one id per row, keeps a shared link short.
  const selectedIds = new Set(params.get('selected') === allLoaded ? items.map((run) => run.id) : params.getAll('selected'))
  const list = runs.query
  const newRows = useNewRows<Run>(runsTopic, (run) => runMatches(run, filter))
  const showLoading = useDelayed(list.isPending)
  const functions = useQuery({ queryKey: ['jobs', 'functions'], queryFn: () => fetchFunctions(api) })
  const commands = useJobsCommands()
  const pending = usePending()
  const [allMatching, setAllMatching] = useState(false)

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

  function select(ids: ReadonlySet<string>) {
    setAllMatching(false)
    update((next) => {
      next.delete('selected')
      next.delete('create')

      if (ids.size > 1 && ids.size === items.length && items.every((run) => ids.has(run.id))) {
        next.set('selected', allLoaded)
      } else {
        ids.forEach((id) => next.append('selected', id))
      }
    })
  }

  function setFilter(name: keyof RunFilter, value: string) {
    setAllMatching(false)
    update((next) => {
      if (value) {
        next.set(name, value)
      } else {
        next.delete(name)
      }

      if (name === 'range') {
        const span = rangeSpans[value]

        if (span) {
          next.set('from', new Date(Date.now() - span).toISOString())
        } else {
          next.delete('from')
        }
      }

      next.delete('selected')
    })
  }

  function clearFilters() {
    setAllMatching(false)
    update((next) => {
      filterNames.forEach((name) => next.delete(name))
      next.delete('selected')
    })
  }

  function openCreate(kind: 'time-job' | 'chain') {
    update((next) => {
      next.delete('selected')
      next.set('create', kind)
    })
  }

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
      start({ module: module.id, command: requeue, filter: operationScope({ ...filter, status: 'Failed' }), noun: runsNoun({ ...filter, status: 'Failed' }) })
    }
    // Runs once per palette request; the filter is read as it is now.
  }, [wantsOperation, requeue])

  useEffect(() => {
    if (!wantsOperation) {
      started.current = false
    }
  }, [wantsOperation])

  const selectedRuns = items.filter((run) => selectedIds.has(run.id))
  const [onlySelected] = selectedIds
  // A link can select a run the loaded page does not hold; the inspector reads that one run by its id instead.
  const lookupId = list.data && selectedIds.size === 1 && selectedRuns.length === 0 ? onlySelected : undefined
  const count = runs.count
  const filtered = isFiltered(filter)
  const addReason = module.canOperate ? null : viewOnlyReason
  const functionOptions = [
    ...new Set([...(functions.data ?? []).map((item) => item.name), ...(filter.function ? [filter.function] : [])]),
  ].map((name) => ({ value: name, label: name }))

  let inspector
  if (creating === 'time-job' || creating === 'chain') {
    inspector = (
      <TimeJobForm
        chain={creating === 'chain'}
        onCancel={() => update((next) => next.delete('create'))}
        onCreated={(id) => {
          void client.invalidateQueries({ queryKey: listKey(runsTopic) })
          select(new Set([id]))
        }}
      />
    )
  } else if (allMatching && count) {
    inspector = <MatchingInspector filter={filter} count={format.count(count)} onClose={() => select(new Set())} />
  } else {
    inspector = (
      <RunsInspector
        runs={selectedRuns}
        lookupId={lookupId}
        count={count ? format.count(count) : undefined}
        pending={pending}
        onClose={() => select(new Set())}
      />
    )
  }

  return (
    <div style={layout}>
      <section aria-labelledby="jobs-runs-title" style={canvas}>
        <header className="hd-actions">
          <h1 id="jobs-runs-title" className="hd-title">
            Runs
          </h1>
          {count ? <span className="hd-hint">{`${format.count(count)} ${count.value === 1 && !count.capped ? 'run' : 'runs'}`}</span> : null}
          <Button variant="primary" disabledReason={addReason} onPress={() => openCreate('time-job')}>
            Add time job
          </Button>
          <Button disabledReason={addReason} onPress={() => openCreate('chain')}>
            Add chain
          </Button>
        </header>
        <div className="hd-actions" role="search" aria-label="Filter runs">
          <SelectField label="Kind" emptyLabel="Any kind" value={filter.kind} onChange={(value) => setFilter('kind', value)} options={kindOptions} />
          <SelectField label="Function" emptyLabel="Any function" value={filter.function} onChange={(value) => setFilter('function', value)} options={functionOptions} />
          <SelectField label="Status" emptyLabel="Any status" value={filter.status} onChange={(value) => setFilter('status', value)} options={statusOptions} />
          <SelectField label="Time range" emptyLabel="Any time" value={filter.range} onChange={(value) => setFilter('range', value)} options={rangeOptions} />
          {filter.correlationId ? <FilterChip label="correlation" value={filter.correlationId} onClear={() => setFilter('correlationId', '')} /> : null}
          {filter.fingerprint ? <FilterChip label="cause" value={filter.fingerprint} onClear={() => setFilter('fingerprint', '')} /> : null}
          {filter.cronJobId ? <FilterChip label="cron job" value={filter.cronJobId} onClear={() => setFilter('cronJobId', '')} /> : null}
        </div>
        <StaleNotice what="these runs" onRefresh={() => void list.refetch()} />
        {list.isPending ? (
          showLoading ? (
            <Skeleton label="Loading runs" rows={6} />
          ) : null
        ) : list.isError ? (
          <LoadError message={list.error.message} onRetry={() => void list.refetch()} />
        ) : (
          <DataTable
            // A new filter is a new list: the held rows and the scroll start again.
            key={filterKey}
            label="Runs"
            viewId="jobs.runs"
            rows={items}
            matches={(run) => runMatches(run, filter)}
            hasMore={runs.hasMore}
            isLoadingMore={runs.isLoadingMore}
            onLoadMore={runs.loadMore}
            newRows={newRows}
            selectionMode="multiple"
            selectedIds={selectedIds}
            onSelectionChange={select}
            matchingCount={count?.value}
            onSelectAllMatching={() => setAllMatching(true)}
            emptyState={
              filtered ? (
                <span>
                  No runs match. Clear filters or widen the time range.{' '}
                  <Button variant="ghost" onPress={clearFilters}>
                    Clear filters
                  </Button>
                </span>
              ) : (
                'No runs yet. A run appears when a host schedules a time job or a cron job fires; add a time job here.'
              )
            }
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
                width: '2fr',
              },
              {
                id: 'status',
                header: 'Status',
                cell: (run) => (
                  <CellText>
                    <StatusMark {...runStatusMark(run, pending.labelOf(run))} />
                  </CellText>
                ),
                width: 130,
              },
              { id: 'kind', header: 'Kind', cell: (run) => kindLabel(run.kind), priority: 2, width: 110 },
              { id: 'retries', header: 'Retries', cell: (run) => format.number(run.retryCount), align: 'end', priority: 2, width: 80 },
              {
                id: 'duration',
                header: 'Duration',
                cell: (run) => (run.executedAt ? formatDuration(run.elapsedMilliseconds, format.number) : ''),
                align: 'end',
                priority: 2,
                width: 100,
              },
              {
                id: 'due',
                header: 'Due',
                cell: (run) => (
                  <CellText>
                    <Time value={run.executionTime ?? run.createdAt} short />
                  </CellText>
                ),
                width: 110,
              },
            ]}
          />
        )}
      </section>
      {inspector}
    </div>
  )
}

/** Every run the filter matches, past the loaded page: each action runs over them as an Operation. */
function MatchingInspector({ filter, count, onClose }: { filter: RunFilter; count: string; onClose: () => void }) {
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
        onPress={() => start({ module: module.id, command, filter: operationScope(filter), noun })}
      >
        {`${command.label} ${count}`}
      </Button>
    ) : null

  return (
    <Inspector label={`All ${count} matching ${noun}`} onClose={onClose}>
      <h2 className="hd-title">{`All ${count} matching ${noun}`}</h2>
      <p className="hd-hint">Each action counts the rows the filter matches at confirm, then runs over them in chunks from this tab.</p>
      <div className="hd-actions">
        {operation(commands.requeue, 'primary')}
        {operation(commands.cancel)}
        {operation(commands.delete, 'danger')}
      </div>
    </Inspector>
  )
}

interface RunsInspectorProps {
  runs: readonly Run[]
  lookupId?: string
  count?: string
  pending: Pending
  onClose: () => void
}

/** The selection's inspector: one run, the shared actions of several, or the view's summary. */
function RunsInspector({ runs, lookupId, count, pending, onClose }: RunsInspectorProps) {
  const [only] = runs

  if (lookupId) {
    return <LookedUpRunInspector id={lookupId} pending={pending} onClose={onClose} />
  }

  if (runs.length === 0) {
    return (
      <Inspector label="Runs summary">
        <h2 className="hd-title">Runs</h2>
        <p>{count ? `${count} runs match this view.` : 'Runs appear here once they load.'}</p>
        <p className="hd-hint">Select a run to see its fields, its attempts, and the actions allowed now.</p>
      </Inspector>
    )
  }

  if (runs.length > 1 || !only) {
    return (
      <Inspector label={`${runs.length} runs selected`} onClose={onClose}>
        <h2 className="hd-title">{`${runs.length} runs selected`}</h2>
        <RunActions runs={runs} pending={pending} />
      </Inspector>
    )
  }

  return <RunInspector run={only} pending={pending} onClose={onClose} />
}

/** A selected run outside the loaded page: read by its id, then shown as any selected run is. */
function LookedUpRunInspector({ id, pending, onClose }: { id: string; pending: Pending; onClose: () => void }) {
  const { api } = useDashboard()
  const detail = useDashboardObject({ topic: runsTopic, id, fetch: () => fetchRun(api, id) })

  if (detail.data) {
    return <RunInspector run={detail.data} pending={pending} onClose={onClose} />
  }

  return (
    <Inspector label={`Run ${id}`} onClose={onClose}>
      <h2 className="hd-title">Run</h2>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <p role="alert">This run no longer exists.</p>
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label="Loading the run" />
      )}
    </Inspector>
  )
}

function RunInspector({ run, pending, onClose }: { run: Run; pending: Pending; onClose: () => void }) {
  const status = runStatusMark(run, pending.labelOf(run))
  useTraceKey(run)

  return (
    <Inspector label={`Run ${run.id}`} onClose={onClose}>
      <ObjectHeader
        title={run.function}
        id={run.id}
        status={status}
        chips={run.tenantId ? <TenantChip tenant={run.tenantId} /> : undefined}
        actions={
          <>
            <Link to={`runs/${encodeURIComponent(run.id)}`}>Open run page</Link>
            <RunActions runs={[run]} pending={pending} />
          </>
        }
      />
      <RunFields run={run} />
      <AttemptTimeline run={run} />
      <RunPayload runId={run.id} />
    </Inspector>
  )
}
