import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { useOperations } from '@headless/dashboard-sdk/commands'
import {
  Button,
  Inspector,
  Mono,
  ObjectHeader,
  PrimaryAction,
  SelectField,
  StatusMark,
  TextInputField,
  useDelayed,
  useViewKeys,
} from '@headless/dashboard-sdk/components'
import { DataTable } from '@headless/dashboard-sdk/table'
import { DashboardApiError, listKey, useDashboardKeysetList, useDashboardObject, useNewRows } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useMutation, useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useSearchParams } from '@headless/dashboard-sdk/router'
import { useEffect, useRef } from 'react'
import { LoadError, RunActions, RunFields, RunTime, RunTimeline, StaleNotice } from '../run-parts.tsx'
import {
  addRun,
  fetchRun,
  fetchRuns,
  runMatches,
  runStatuses,
  runStatusMark,
  runsTopic,
  useRequeuing,
  useRunCommands,
  viewOnlyReason,
  type Run,
  type RunFilter,
} from '../runs.ts'

const statusOptions = [{ value: '', label: 'Any status' }, ...runStatuses.map((status) => ({ value: status, label: status }))]

/** The canvas sits beside the inspector, and the two stack once the screen is too narrow for both. */
const layout = { display: 'flex', flexWrap: 'wrap', gap: '1rem', alignItems: 'start' } as const
const canvas = { flex: '1 1 32rem', minInlineSize: 0, display: 'flex', flexDirection: 'column', gap: '0.75rem' } as const

/** The diagnostic runs: a live list, the selection's inspector, and Requeue over the failed runs as an Operation. */
export default function RunsList() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const client = useQueryClient()
  const { start } = useOperations()
  const [params, setParams] = useSearchParams()
  const filter: RunFilter = { status: params.get('status') ?? '', error: params.get('error') ?? '' }
  const selectedIds = new Set(params.getAll('selected'))
  const topic = runsTopic(module.id)
  const runs = useDashboardKeysetList<Run>({ topic, params: filter, fetchPage: (cursor) => fetchRuns(api, module.id, filter, cursor) })
  const list = runs.query
  const newRows = useNewRows(topic)
  const showLoading = useDelayed(list.isPending)
  const { requeue } = useRunCommands()
  const requeuing = useRequeuing()
  const add = useMutation({
    mutationFn: () => addRun(api, module.id),
    onSuccess: async (run) => {
      await client.invalidateQueries({ queryKey: listKey(topic) })
      select(new Set([run.id]))
    },
  })

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
    update((next) => {
      next.delete('selected')
      ids.forEach((id) => next.append('selected', id))
    })
  }

  function setFilter(name: keyof RunFilter, value: string) {
    update((next) => {
      if (value) {
        next.set(name, value)
      } else {
        next.delete(name)
      }

      next.delete('selected')
    })
  }

  const failedFilter = { status: 'Failed', ...(filter.error ? { error: filter.error } : {}) }
  const matchingFilter = { ...(filter.status ? { status: filter.status } : {}), ...(filter.error ? { error: filter.error } : {}) }

  function requeueFailed() {
    if (requeue) {
      start({ module: module.id, command: requeue, filter: failedFilter, noun: 'failed runs' })
    }
  }

  // The palette's `Requeue failed runs` opens this view with `operation=requeue`; the view starts the Operation once.
  const started = useRef(false)
  const wantsOperation = params.get('operation') === 'requeue'

  useEffect(() => {
    if (!wantsOperation || !requeue || started.current) {
      return
    }

    started.current = true
    update((next) => next.delete('operation'))

    if (requeue.canRun) {
      start({ module: module.id, command: requeue, filter: failedFilter, noun: 'failed runs' })
    }
    // Runs once per palette request; the filter and helpers are read as they are now.
  }, [wantsOperation, requeue])

  useEffect(() => {
    if (!wantsOperation) {
      started.current = false
    }
  }, [wantsOperation])

  const items = runs.rows
  const selectedRuns = items.filter((run) => selectedIds.has(run.id))
  // A link can select a run the loaded page does not hold; the inspector reads that one run by its id instead.
  const [onlySelected] = selectedIds
  const lookupId = list.data && selectedIds.size === 1 && selectedRuns.length === 0 ? onlySelected : undefined
  const count = runs.count
  const filtered = Boolean(filter.status || filter.error)
  const addReason = module.canOperate ? null : viewOnlyReason
  const requeueReason = requeue && !requeue.canRun ? (requeue.disabledReason ?? viewOnlyReason) : null

  // `/` focuses the view's filter: the free-text one, which is what an operator types into.
  const filters = useRef<HTMLDivElement>(null)
  useViewKeys({ filter: () => filters.current?.querySelector<HTMLInputElement>('input')?.focus() })

  return (
    <div style={layout}>
      <section aria-labelledby="diagnostics-runs-title" style={canvas}>
        <header className="hd-actions">
          <h1 id="diagnostics-runs-title" className="hd-title">
            Runs
          </h1>
          {count ? <span className="hd-hint">{`${format.count(count)} ${count.value === 1 && !count.capped ? 'run' : 'runs'}`}</span> : null}
          <PrimaryAction>
            <Button variant="primary" disabledReason={addReason} isPending={add.isPending} onPress={() => add.mutate()}>
              Add run
            </Button>
          </PrimaryAction>
          <Button disabledReason={requeueReason} onPress={requeueFailed}>
            Requeue failed runs
          </Button>
        </header>
        {add.isError ? <LoadError message={add.error.message} onRetry={() => add.mutate()} /> : null}
        <div ref={filters} className="hd-actions" role="search" aria-label="Filter runs">
          <SelectField label="Status" value={filter.status} onChange={(value) => setFilter('status', value)} options={statusOptions} />
          <TextInputField label="Error contains" value={filter.error} onChange={(value) => setFilter('error', value)} />
        </div>
        <StaleNotice onRefresh={() => void list.refetch()} />
        {list.isPending ? (
          showLoading ? (
            <div role="status" aria-busy="true" aria-label="Loading runs" className="hd-table-wrap">
              <p className="hd-hint">Loading runs…</p>
            </div>
          ) : null
        ) : list.isError ? (
          <LoadError message={list.error.message} onRetry={() => void list.refetch()} />
        ) : (
          <DataTable
            // A new filter is a new list: the held rows and the scroll start again.
            key={`${filter.status}\n${filter.error}`}
            label="Runs"
            viewId={`${module.id}.runs`}
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
            onSelectAllMatching={() => {
              if (requeue?.canRun) {
                start({ module: module.id, command: requeue, filter: matchingFilter, noun: 'matching runs' })
              }
            }}
            emptyState={filtered ? 'No runs match this filter. Clear it to see every run.' : 'No runs yet - add one with Fail on a new run'}
            columns={[
              { id: 'id', header: 'Run', cell: (run) => <Mono>{run.id}</Mono>, isRowHeader: true, width: 140 },
              {
                id: 'status',
                header: 'Status',
                cell: (run) => <StatusMark {...runStatusMark(run.status, requeuing.isRequeuing(run))} />,
                width: 130,
              },
              { id: 'attempts', header: 'Attempts', cell: (run) => format.number(run.attempts), align: 'end', priority: 2, width: 90 },
              { id: 'lastError', header: 'Last error', cell: (run) => run.lastError ?? '', priority: 2, width: '2fr' },
              { id: 'updated', header: 'Updated', cell: (run) => <RunTime value={run.updatedAt} />, width: '1fr' },
            ]}
          />
        )}
      </section>
      <RunsInspector
        runs={selectedRuns}
        lookupId={lookupId}
        count={count ? format.count(count) : undefined}
        isRequeuing={requeuing.isRequeuing}
        onRequeued={requeuing.mark}
        onClose={() => select(new Set())}
      />
    </div>
  )
}

interface RunsInspectorProps {
  runs: readonly Run[]
  /** The one selected run's id when the loaded page does not hold it. */
  lookupId?: string
  count?: string
  isRequeuing: (run: Run) => boolean
  onRequeued: (ids: readonly string[], versions: ReadonlyMap<string, string>) => void
  onClose: () => void
}

/** The selection's inspector: one run's fields and timeline, the shared actions of several, or the view's summary. */
function RunsInspector({ runs, lookupId, count, isRequeuing, onRequeued, onClose }: RunsInspectorProps) {
  const [only] = runs

  if (lookupId) {
    return <LookedUpRunInspector id={lookupId} isRequeuing={isRequeuing} onRequeued={onRequeued} onClose={onClose} />
  }

  if (runs.length === 0) {
    return (
      <Inspector label="Runs summary">
        <h2 className="hd-title">Runs</h2>
        <p>{count ? `${count} runs match this view.` : 'Runs appear here once they load.'}</p>
        <p className="hd-hint">Select a run to see its fields, its timeline, and the actions allowed now.</p>
      </Inspector>
    )
  }

  if (runs.length > 1 || !only) {
    return (
      <Inspector label={`${runs.length} runs selected`} onClose={onClose}>
        <h2 className="hd-title">{`${runs.length} runs selected`}</h2>
        <RunActions runs={runs} onRequeued={onRequeued} />
      </Inspector>
    )
  }

  return <RunInspector run={only} requeuing={isRequeuing(only)} onRequeued={onRequeued} onClose={onClose} />
}

/** A selected run outside the loaded page: read by its id, then shown as any selected run is. */
function LookedUpRunInspector({
  id,
  isRequeuing,
  onRequeued,
  onClose,
}: { id: string } & Pick<RunsInspectorProps, 'isRequeuing' | 'onRequeued' | 'onClose'>) {
  const { api } = useDashboard()
  const module = useModule()
  const detail = useDashboardObject({ topic: runsTopic(module.id), id, fetch: () => fetchRun(api, module.id, id) })

  if (detail.data) {
    return <RunInspector run={detail.data} requeuing={isRequeuing(detail.data)} onRequeued={onRequeued} onClose={onClose} />
  }

  return (
    <Inspector label={`Run ${id}`} onClose={onClose}>
      <h2 className="hd-title">Run</h2>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <p role="alert">{`No run ${id}. It may have been removed with the in-memory store.`}</p>
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <p className="hd-hint" role="status">
          Loading the run…
        </p>
      )}
    </Inspector>
  )
}

function RunInspector({
  run,
  requeuing,
  onRequeued,
  onClose,
}: {
  run: Run
  requeuing: boolean
  onRequeued: RunsInspectorProps['onRequeued']
  onClose: () => void
}) {
  const { api } = useDashboard()
  const module = useModule()
  const detail = useDashboardObject({ topic: runsTopic(module.id), id: run.id, fetch: () => fetchRun(api, module.id, run.id) })
  // The list row fills the fields at once from the cache; the detail adds the timeline when it answers.
  const status = runStatusMark(run.status, requeuing)
  const seenVersion = useRef(run.version)

  // A live change patches the row's fields but carries no timeline, so the timeline is read again.
  useEffect(() => {
    if (seenVersion.current !== run.version) {
      seenVersion.current = run.version
      void detail.refetch()
    }
  }, [run.version, detail])

  return (
    <Inspector label={`Run ${run.id}`} onClose={onClose}>
      <ObjectHeader
        title="Run"
        id={run.id}
        status={status}
        actions={
          <>
            <Link to={`runs/${encodeURIComponent(run.id)}`}>Open run page</Link>
            <RunActions runs={[run]} onRequeued={onRequeued} />
          </>
        }
      />
      <RunFields run={run} statusLabel={status.label} />
      {detail.data ? (
        <RunTimeline events={detail.data.timeline} />
      ) : detail.isError ? (
        <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
      ) : (
        <p className="hd-hint" role="status">
          Loading the timeline…
        </p>
      )}
    </Inspector>
  )
}
