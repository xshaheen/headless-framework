import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { useOperations } from '@headless/dashboard-sdk/commands'
import {
  Button,
  DataTable,
  Inspector,
  NewRowsNotice,
  ObjectHeader,
  SelectField,
  StatusMark,
  TextInputField,
  useDelayed,
} from '@headless/dashboard-sdk/components'
import { formatCount, listKey, useDashboardList, useDashboardObject } from '@headless/dashboard-sdk/data'
import { useMutation, useQueryClient } from '@headless/dashboard-sdk/query'
import { Link, useNavigate, useSearchParams } from '@headless/dashboard-sdk/router'
import { useEffect, useRef, type KeyboardEvent } from 'react'
import { LoadError, RunActions, RunFields, RunTime, RunTimeline, StaleNotice } from '../run-parts.tsx'
import {
  addRun,
  fetchRun,
  fetchRuns,
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

function isTextField(target: EventTarget) {
  return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
}

/** The diagnostic runs: a live list, the selection's inspector, and Requeue over the failed runs as an Operation. */
export default function RunsList() {
  const { api } = useDashboard()
  const module = useModule()
  const client = useQueryClient()
  const navigate = useNavigate()
  const { start } = useOperations()
  const [params, setParams] = useSearchParams()
  const filter: RunFilter = { status: params.get('status') ?? '', error: params.get('error') ?? '' }
  const selectedIds = new Set(params.getAll('selected'))
  const topic = runsTopic(module.id)
  const runs = useDashboardList<Run>({ topic, params: filter, fetch: () => fetchRuns(api, module.id, filter) })
  const showLoading = useDelayed(runs.isPending)
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

  const items = runs.data?.items ?? []
  const selectedRuns = items.filter((run) => selectedIds.has(run.id))
  const count = runs.data?.count
  const filtered = Boolean(filter.status || filter.error)
  const addReason = module.canOperate ? null : viewOnlyReason
  const requeueReason = requeue && !requeue.canRun ? (requeue.disabledReason ?? viewOnlyReason) : null

  // `X` toggles the focused row in the selection, as the dashboard's lists do.
  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key.toLowerCase() !== 'x' || event.metaKey || event.ctrlKey || event.altKey || isTextField(event.target)) {
      return
    }

    const row = (event.target as HTMLElement).closest('[role="row"]')
    const id = row?.querySelector<HTMLElement>('[data-run-id]')?.dataset.runId

    if (!id) {
      return
    }

    event.preventDefault()
    const next = new Set(selectedIds)

    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }

    select(next)
  }

  return (
    <div style={layout}>
      <section aria-labelledby="diagnostics-runs-title" style={canvas}>
        <header className="hd-actions">
          <h1 id="diagnostics-runs-title" className="hd-title">
            Runs
          </h1>
          {count ? <span className="hd-hint">{`${formatCount(count)} ${count.value === 1 && !count.capped ? 'run' : 'runs'}`}</span> : null}
          <Button variant="primary" disabledReason={addReason} isPending={add.isPending} onPress={() => add.mutate()}>
            Add run
          </Button>
          <Button disabledReason={requeueReason} onPress={requeueFailed}>
            Requeue failed runs
          </Button>
        </header>
        {add.isError ? <LoadError message={add.error.message} onRetry={() => add.mutate()} /> : null}
        <div className="hd-actions" role="search" aria-label="Filter runs">
          <SelectField label="Status" value={filter.status} onChange={(value) => setFilter('status', value)} options={statusOptions} />
          <TextInputField label="Error contains" value={filter.error} onChange={(value) => setFilter('error', value)} />
        </div>
        <StaleNotice onRefresh={() => void runs.refetch()} />
        <NewRowsNotice topic={topic} />
        {runs.isPending ? (
          showLoading ? (
            <div role="status" aria-busy="true" aria-label="Loading runs" className="hd-table-wrap">
              <p className="hd-hint">Loading runs…</p>
            </div>
          ) : null
        ) : runs.isError ? (
          <LoadError message={runs.error.message} onRetry={() => void runs.refetch()} />
        ) : (
          <div onKeyDown={onKeyDown}>
            <DataTable
              label="Runs"
              rows={items}
              selectionMode="multiple"
              selectedIds={selectedIds}
              onSelectionChange={select}
              onOpen={(run) => navigate(`runs/${encodeURIComponent(run.id)}`)}
              matchingCount={count?.value}
              onSelectAllMatching={() => {
                if (requeue?.canRun) {
                  start({ module: module.id, command: requeue, filter: matchingFilter, noun: 'matching runs' })
                }
              }}
              emptyState={filtered ? 'No runs match this filter. Clear it to see every run.' : 'No runs yet - add one with Fail on a new run'}
              columns={[
                { id: 'id', header: 'Run', cell: (run) => (
                    <bdi className="hd-mono" data-run-id={run.id}>
                      {run.id}
                    </bdi>
                  ),
                  isRowHeader: true },
                {
                  id: 'status',
                  header: 'Status',
                  cell: (run) => <StatusMark {...runStatusMark(run.status, requeuing.isRequeuing(run))} />,
                },
                { id: 'attempts', header: 'Attempts', cell: (run) => run.attempts, align: 'end' },
                { id: 'lastError', header: 'Last error', cell: (run) => run.lastError ?? '' },
                { id: 'updated', header: 'Updated', cell: (run) => <RunTime value={run.updatedAt} /> },
              ]}
            />
          </div>
        )}
      </section>
      <RunsInspector
        runs={selectedRuns}
        count={count ? formatCount(count) : undefined}
        isRequeuing={requeuing.isRequeuing}
        onRequeued={requeuing.mark}
        onClose={() => select(new Set())}
      />
    </div>
  )
}

interface RunsInspectorProps {
  runs: readonly Run[]
  count?: string
  isRequeuing: (run: Run) => boolean
  onRequeued: (ids: readonly string[], versions: ReadonlyMap<string, string>) => void
  onClose: () => void
}

/** The selection's inspector: one run's fields and timeline, the shared actions of several, or the view's summary. */
function RunsInspector({ runs, count, isRequeuing, onRequeued, onClose }: RunsInspectorProps) {
  const [only] = runs

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
        actions={<RunActions runs={[run]} onRequeued={onRequeued} />}
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
      <Link to={`runs/${encodeURIComponent(run.id)}`}>Open run page</Link>
    </Inspector>
  )
}
