import { useDashboard } from '@headless/dashboard-sdk'
import { Inspector, Mono, ObjectHeader, TenantChip, useDelayed } from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardObject } from '@headless/dashboard-sdk/data'
import { Link, useParams, useSearchParams } from '@headless/dashboard-sdk/router'
import {
  AttemptFields,
  AttemptTimeline,
  canvas,
  layout,
  LoadError,
  RunActions,
  RunFields,
  RunPayload,
  Skeleton,
  StaleNotice,
  useAttempts,
  useTraceKey,
} from '../parts.tsx'
import { fetchRun, runStatusMark, runsTopic, usePending } from '../runs.ts'

/** One run's page: its header and actions, fields, attempt timeline, chain steps, and payload on request. */
export default function RunPage() {
  const { api } = useDashboard()
  const runId = useParams().runId ?? ''
  const run = useDashboardObject({ topic: runsTopic, id: runId, fetch: () => fetchRun(api, runId) })
  const showLoading = useDelayed(run.isPending)
  const pending = usePending()
  const [params, setParams] = useSearchParams()
  const attemptId = params.get('attempt') ?? undefined
  useTraceKey(run.data)

  const back = <Link to="/jobs/">Back to runs</Link>

  if (run.isPending) {
    return showLoading ? (
      <div className="hd-fields">
        <Skeleton label="Loading the run" rows={2} />
        <Skeleton label="Loading the timeline" rows={4} />
      </div>
    ) : null
  }

  if (run.isError) {
    if (run.error instanceof DashboardApiError && run.error.status === 404) {
      return (
        <section aria-label="Run not found" className="hd-fields">
          <p role="alert">This run no longer exists.</p>
          {back}
        </section>
      )
    }

    return <LoadError message={run.error.message} onRetry={() => void run.refetch()} />
  }

  const detail = run.data

  return (
    <div style={layout}>
      <article aria-label={`Run ${detail.id}`} style={canvas}>
        {back}
        <ObjectHeader
          title={detail.function}
          id={detail.id}
          status={runStatusMark(detail, pending.labelOf(detail))}
          chips={detail.tenantId ? <TenantChip tenant={detail.tenantId} /> : undefined}
          actions={<RunActions runs={[detail]} pending={pending} />}
        />
        <StaleNotice what="this run" onRefresh={() => void run.refetch()} />
        <RunFields run={detail} />
        <AttemptTimeline
          run={detail}
          selected={attemptId}
          onSelect={(id) => setParams(id === 'created' ? {} : { attempt: id }, { replace: true })}
        />
        {detail.children.length > 0 ? (
          <section aria-label="Chain" className="hd-fields">
            <h3 className="hd-title">Steps after this run</h3>
            <ul>
              {detail.children.map((child) => (
                <li key={child}>
                  <Link to={`/jobs/runs/${encodeURIComponent(child)}`}>
                    <Mono>{child}</Mono>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ) : null}
        <RunPayload runId={detail.id} />
      </article>
      <AttemptInspector run={detail} attemptId={attemptId} onClose={() => setParams({}, { replace: true })} />
    </div>
  )
}

/** The selected attempt, or the page's summary when none is selected. */
function AttemptInspector({ run, attemptId, onClose }: { run: { id: string; version: string }; attemptId?: string; onClose: () => void }) {
  const attempts = useAttempts(run)
  const attempt = attemptId === undefined ? undefined : attempts.data?.find((item) => String(item.number) === attemptId)

  if (!attempt) {
    return (
      <Inspector label="Run summary">
        <h2 className="hd-title">Run</h2>
        <p className="hd-hint">Select an attempt in the timeline to see its node, times, and exception.</p>
      </Inspector>
    )
  }

  return (
    <Inspector label={`Attempt ${attempt.number}`} onClose={onClose}>
      <h2 className="hd-title">{`Attempt ${attempt.number}`}</h2>
      <AttemptFields attempt={attempt} />
    </Inspector>
  )
}
