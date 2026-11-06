import { useDashboard } from '@headless/dashboard-sdk'
import { useDelayed } from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardObject } from '@headless/dashboard-sdk/data'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { LoadError, RunPanel, Skeleton, StaleNotice } from '../parts.tsx'
import { fetchRun, runsTopic, usePending } from '../runs.ts'

/** One run's own page: the run panel's content and actions, detached from its list. */
export default function RunPage() {
  const { api } = useDashboard()
  const runId = useParams().runId ?? ''
  const run = useDashboardObject({ topic: runsTopic, id: runId, fetch: () => fetchRun(api, runId) })
  const showLoading = useDelayed(run.isPending)
  const pending = usePending()

  if (run.isPending) {
    return showLoading ? <Skeleton label="Loading the run" rows={4} /> : null
  }

  if (run.isError) {
    if (run.error instanceof DashboardApiError && run.error.status === 404) {
      return (
        <section aria-label="Run not found">
          <p role="alert">This run no longer exists.</p>
          <Link to="/jobs/runs">Back to runs</Link>
        </section>
      )
    }

    return <LoadError message={run.error.message} onRetry={() => void run.refetch()} />
  }

  return (
    <>
      <StaleNotice what="this run" onRefresh={() => void run.refetch()} />
      <RunPanel run={run.data} pending={pending} page />
    </>
  )
}
