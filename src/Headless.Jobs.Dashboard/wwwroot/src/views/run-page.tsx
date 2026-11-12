import { useDashboard } from '@headless/dashboard-sdk'
import { EmptyState, usePageTitle } from '@headless/dashboard-sdk/components'
import { RunsIcon } from '@headless/dashboard-sdk/icons'
import { DashboardApiError, useDashboardObject } from '@headless/dashboard-sdk/data'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { QueryState, RunPanel, StaleNotice } from '../parts.tsx'
import { fetchRun, runsTopic, usePending } from '../runs.ts'

export default function RunPage() {
  const { api } = useDashboard()
  const runId = useParams().runId ?? ''
  const run = useDashboardObject({ topic: runsTopic, id: runId, fetch: () => fetchRun(api, runId) })
  const pending = usePending()
  const isGone = run.error instanceof DashboardApiError && run.error.status === 404

  // Named by its function, as the runs list names it; a failed read still names the page, so the shell does not wait.
  usePageTitle(isGone ? 'Run not found' : run.data ? `${run.data.function} run` : run.error ? 'Run' : undefined)

  if (isGone) {
    return (
      <section aria-label="Run not found">
        <EmptyState
          role="alert"
          placement="page"
          icon={RunsIcon}
          title="This run no longer exists."
          action={<Link to="/jobs/runs">Back to runs</Link>}
        >
          It was deleted, or its storage no longer holds it.
        </EmptyState>
      </section>
    )
  }

  return (
    <QueryState query={run} label="Loading the run" rows={4}>
      {(data) => (
        <>
          <StaleNotice what="this run" onRefresh={() => void run.refetch()} failed={run.isError} />
          <RunPanel run={data} pending={pending} page />
        </>
      )}
    </QueryState>
  )
}
