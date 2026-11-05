import { useDashboard, useModule } from '@headless/dashboard-sdk'
import {
  Button,
  Inspector,
  InspectorField,
  InspectorFields,
  ObjectHeader,
  PayloadView,
  useDelayed,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardObject } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useParams, useSearchParams } from '@headless/dashboard-sdk/router'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import { useState } from 'react'
import { LoadError, RunActions, RunFields, RunTime, RunTimeline, StaleNotice } from '../run-parts.tsx'
import { fetchRun, runStatusMark, runsTopic, useRequeuing } from '../runs.ts'

interface PayloadAnswer {
  content: string
  contentType: string
  headers?: Readonly<Record<string, string>>
}

/** The page sits beside the inspector, and the two stack once the screen is too narrow for both. */
const layout = { display: 'flex', flexWrap: 'wrap', gap: '1rem', alignItems: 'start' } as const
const canvas = { flex: '1 1 32rem', minInlineSize: 0 } as const

/** One run's page: its header and actions, fields, timeline, and payload on request. */
export default function RunPage() {
  const { api } = useDashboard()
  const module = useModule()
  const runId = useParams().runId ?? ''
  const topic = runsTopic(module.id)
  const run = useDashboardObject({ topic, id: runId, fetch: () => fetchRun(api, module.id, runId) })
  const showLoading = useDelayed(run.isPending)
  const requeuing = useRequeuing()
  const [params, setParams] = useSearchParams()
  const eventId = params.get('event') ?? undefined

  function selectEvent(id: string | undefined) {
    setParams(id === undefined ? {} : { event: id }, { replace: true })
  }

  // A live change patches the fields but carries no timeline, so the page reads the run again for it.
  useStreamEvent('change', (change) => {
    if (change.topic === topic && change.id === runId) {
      void run.refetch()
    }
  })

  const back = (
    <Link to="../.." relative="path">
      Back to runs
    </Link>
  )

  if (run.isPending) {
    return showLoading ? (
      <p role="status" aria-busy="true" className="hd-hint">
        Loading the run…
      </p>
    ) : null
  }

  if (run.isError) {
    if (run.error instanceof DashboardApiError && run.error.status === 404) {
      return (
        <section aria-label="Run not found">
          <p role="alert">{`No run ${runId}. It may have been removed with the in-memory store.`}</p>
          {back}
        </section>
      )
    }

    return <LoadError message={run.error.message} onRetry={() => void run.refetch()} />
  }

  const detail = run.data
  const status = runStatusMark(detail.status, requeuing.isRequeuing(detail))

  const event = eventId === undefined ? undefined : detail.timeline[Number(eventId)]

  return (
    <div style={layout}>
      <article aria-label={`Run ${detail.id}`} className="hd-fields" style={canvas}>
        {back}
        <ObjectHeader title="Run" id={detail.id} status={status} actions={<RunActions runs={[detail]} onRequeued={requeuing.mark} />} />
        <StaleNotice onRefresh={() => void run.refetch()} />
        <RunFields run={detail} statusLabel={status.label} />
        <RunTimeline events={detail.timeline} selected={eventId} onSelect={selectEvent} />
        <RunPayload module={module.id} runId={detail.id} />
      </article>
      {event ? (
        <Inspector label={`Event ${event.kind}`} onClose={() => selectEvent(undefined)}>
          <h2 className="hd-title">{event.kind}</h2>
          <InspectorFields>
            <InspectorField label="At">
              <RunTime value={event.at} />
            </InspectorField>
            <InspectorField label="Detail">{event.text ?? 'None'}</InspectorField>
          </InspectorFields>
        </Inspector>
      ) : (
        <Inspector label="Run summary">
          <h2 className="hd-title">Run</h2>
          <p className="hd-hint">Select a timeline event to see what it recorded.</p>
        </Inspector>
      )}
    </div>
  )
}

/** The run's payload, fetched only when asked for and already redacted by the server. */
function RunPayload({ module, runId }: { module: string; runId: string }) {
  const { api } = useDashboard()
  const [shown, setShown] = useState(false)
  const payload = useQuery({
    queryKey: ['payload', module, 'run', runId],
    queryFn: () => api.request<PayloadAnswer>({ method: 'GET', path: 'payload', query: { module, kind: 'run', id: runId } }),
    enabled: shown,
  })

  return (
    <section aria-label="Payload">
      <h3 className="hd-title">Payload</h3>
      {!shown ? (
        <Button onPress={() => setShown(true)}>Show payload</Button>
      ) : payload.isPending ? (
        <p role="status" className="hd-hint">
          Loading the payload…
        </p>
      ) : payload.isError ? (
        <LoadError message={payload.error.message} onRetry={() => void payload.refetch()} />
      ) : (
        <>
          {payload.data.headers && Object.keys(payload.data.headers).length > 0 ? (
            <InspectorFields>
              {Object.entries(payload.data.headers).map(([name, value]) => (
                <InspectorField key={name} label={name}>
                  <bdi className="hd-mono">{value}</bdi>
                </InspectorField>
              ))}
            </InspectorFields>
          ) : null}
          <PayloadView content={payload.data.content} contentType={payload.data.contentType} />
        </>
      )}
    </section>
  )
}
