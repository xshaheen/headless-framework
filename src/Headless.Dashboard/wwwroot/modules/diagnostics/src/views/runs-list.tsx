import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { useOperations } from '@headless/dashboard-sdk/commands'
import {
  Button,
  DetailPanel,
  Mono,
  PageFrame,
  PageHeader,
  SelectField,
  StatusMark,
  TextInputField,
  useDelayed,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { DataTable } from '@headless/dashboard-sdk/table'
import { DashboardApiError, listKey, useDashboardKeysetList, useDashboardObject, useNewRows } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useMutation, useQueryClient } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { useEffect, useRef } from 'react'
import { LoadError, RunActions, RunPanel, RunTime, StaleNotice, type RunActionsProps } from '../run-parts.tsx'
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

const statusOptions = runStatuses.map((status) => ({ value: status, label: status }))

export default function RunsList() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const client = useQueryClient()
  const { start } = useOperations()
  const [params, setParams] = useSearchParams()
  const filter: RunFilter = { status: params.get('status') ?? '', error: params.get('error') ?? '' }
  const topic = runsTopic(module.id)
  const runs = useDashboardKeysetList<Run>({ topic, params: filter, fetchPage: (cursor) => fetchRuns(api, module.id, filter, cursor) })
  const list = runs.query
  const items = runs.rows
  const matches = (run: Run) => runMatches(run, filter)
  const frame = usePageFrame({ viewId: `${module.id}.runs`, rows: items, matches })
  const newRows = useNewRows(topic)
  const showLoading = useDelayed(list.isPending)
  const { requeue } = useRunCommands()
  const requeuing = useRequeuing()
  const add = useMutation({
    mutationFn: () => addRun(api, module.id),
    onSuccess: async (run) => {
      await client.invalidateQueries({ queryKey: listKey(topic) })
      frame.open(run.id)
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

  function setFilter(name: keyof RunFilter, value: string) {
    frame.setChecked(new Set())
    update((next) => {
      if (value) {
        next.set(name, value)
      } else {
        next.delete(name)
      }
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
      requeueFailed()
    }
    // Runs once per palette request; the filter and helpers are read as they are now.
  }, [wantsOperation, requeue])

  useEffect(() => {
    if (!wantsOperation) {
      started.current = false
    }
  }, [wantsOperation])

  const checkedRuns = items.filter((run) => frame.checked.has(run.id))
  const openRun = items.find((run) => run.id === frame.openId)
  const count = runs.count
  const filtered = Boolean(filter.status || filter.error)
  const addReason = module.canOperate ? null : viewOnlyReason
  const requeueReason = requeue && !requeue.canRun ? (requeue.disabledReason ?? viewOnlyReason) : null

  let panel
  if (frame.openId) {
    // A link can open a run the loaded page does not hold; the panel then reads that one run by its id.
    panel = openRun ? (
      <RunPanel run={openRun} requeuing={requeuing.isRequeuing(openRun)} onRequeued={requeuing.mark} />
    ) : (
      <LookedUpRun id={frame.openId} isRequeuing={requeuing.isRequeuing} onRequeued={requeuing.mark} />
    )
  }

  return (
    <PageFrame
      frame={frame}
      header={
        <PageHeader
          title="Runs"
          count={count ? format.count(count) : undefined}
          description="Development-only runs that exercise the dashboard's commands."
          actions={
            <>
              {checkedRuns.length > 0 ? (
                <>
                  <span>{`${checkedRuns.length} selected`}</span>
                  <RunActions runs={checkedRuns} onRequeued={requeuing.mark} />
                </>
              ) : null}
              <Button variant="primary" disabledReason={addReason} isPending={add.isPending} onPress={() => add.mutate()}>
                Add run
              </Button>
              <Button disabledReason={requeueReason} onPress={requeueFailed}>
                Requeue failed runs
              </Button>
            </>
          }
        >
          <SelectField label="Status" emptyLabel="Any status" value={filter.status} onChange={(value) => setFilter('status', value)} options={statusOptions} />
          <TextInputField label="Error contains" value={filter.error} onChange={(value) => setFilter('error', value)} />
        </PageHeader>
      }
      panel={panel}
    >
      {add.isError ? <LoadError message={add.error.message} onRetry={() => add.mutate()} /> : null}
      <StaleNotice onRefresh={() => void list.refetch()} />
      {list.isPending ? (
        showLoading ? (
          <p role="status" aria-busy="true" aria-label="Loading runs">
            Loading runs…
          </p>
        ) : null
      ) : list.isError ? (
        <LoadError message={list.error.message} onRetry={() => void list.refetch()} />
      ) : (
        <DataTable
          // A new filter is a new list: the held rows and the scroll start again.
          key={`${filter.status}\n${filter.error}`}
          label="Runs"
          viewId={`${module.id}.runs`}
          frame={frame}
          rows={items}
          matches={matches}
          hasMore={runs.hasMore}
          isLoadingMore={runs.isLoadingMore}
          onLoadMore={runs.loadMore}
          newRows={newRows}
          selectionMode="multiple"
          matchingCount={count?.value}
          onSelectAllMatching={() => {
            if (requeue?.canRun) {
              start({ module: module.id, command: requeue, filter: matchingFilter, noun: 'matching runs' })
            }
          }}
          emptyState={filtered ? 'No runs match this filter. Clear it to see every run.' : 'No runs yet. Add run creates one.'}
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
    </PageFrame>
  )
}

function LookedUpRun({
  id,
  isRequeuing,
  onRequeued,
}: {
  id: string
  isRequeuing: (run: Run) => boolean
  onRequeued: RunActionsProps['onRequeued']
}) {
  const { api } = useDashboard()
  const module = useModule()
  const detail = useDashboardObject({ topic: runsTopic(module.id), id, fetch: () => fetchRun(api, module.id, id) })

  if (detail.data) {
    return <RunPanel run={detail.data} requeuing={isRequeuing(detail.data)} onRequeued={onRequeued} />
  }

  return (
    <DetailPanel label={`Run ${id}`} name="Run" id={id}>
      {detail.isError ? (
        detail.error instanceof DashboardApiError && detail.error.status === 404 ? (
          <p role="alert">{`No run ${id}. It may have been removed with the in-memory store.`}</p>
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <p role="status">Loading the run…</p>
      )}
    </DetailPanel>
  )
}
