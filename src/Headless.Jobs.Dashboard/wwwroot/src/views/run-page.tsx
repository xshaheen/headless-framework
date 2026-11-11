import { useDashboard } from '@headless/dashboard-sdk'
import { EmptyState } from '@headless/dashboard-sdk/components'
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

  if (run.error instanceof DashboardApiError && run.error.status === 404) {
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
          <StaleNotice what="this run" onRefresh={() => void run.refetch()} />
          <RunPanel run={data} pending={pending} page />
        </>
      )}
    </QueryState>
  )
}
