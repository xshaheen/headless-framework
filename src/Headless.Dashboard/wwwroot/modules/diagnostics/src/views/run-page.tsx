import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { Button, FieldGrid, GridField, PayloadView, useDelayed } from '@headless/dashboard-sdk/components'
import { DashboardApiError, useDashboardObject } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'
import { Link, useParams, useSearchParams } from '@headless/dashboard-sdk/router'
import { useStreamEvent } from '@headless/dashboard-sdk/stream'
import { useState } from 'react'
import { LoadError, RunPanel, RunTime, StaleNotice } from '../run-parts.tsx'
import { fetchRun, runsTopic, useRequeuing } from '../runs.ts'

interface PayloadAnswer {
  content: string
  contentType: string
  headers?: Readonly<Record<string, string>>
}

/** One run's own page: the run panel detached from its list, with the payload on request and the selected event. */
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

  // A live change patches the fields but carries no timeline, so the page reads the run again for it.
  useStreamEvent('change', (change) => {
    if (change.topic === topic && change.id === runId) {
      void run.refetch()
    }
  })

  if (run.isPending) {
    return showLoading ? (
      <p role="status" aria-busy="true">
        Loading the run…
      </p>
    ) : null
  }

  if (run.isError) {
    if (run.error instanceof DashboardApiError && run.error.status === 404) {
      return (
        <section aria-label="Run not found">
          <p role="alert">{`No run ${runId}. It may have been removed with the in-memory store.`}</p>
          <Link to="../.." relative="path">
            Back to runs
          </Link>
        </section>
      )
    }

    return <LoadError message={run.error.message} onRetry={() => void run.refetch()} />
  }

  const event = eventId === undefined ? undefined : run.data.timeline[Number(eventId)]

  return (
    <>
      <StaleNotice onRefresh={() => void run.refetch()} />
      <RunPanel
        run={run.data}
        requeuing={requeuing.isRequeuing(run.data)}
        onRequeued={requeuing.mark}
        page
        // The selected event is in the URL as `?event=`, so a link can name it.
        selectedEvent={eventId}
        onSelectEvent={(id) => setParams({ event: id }, { replace: true })}
        eventDetail={
          event ? (
            <section aria-label={`Event ${event.kind}`}>
              <FieldGrid columns={2}>
                <GridField label="Event">{event.kind}</GridField>
                <GridField label="At">
                  <RunTime value={event.at} />
                </GridField>
                <GridField label="Detail" span={2}>
                  {event.text ?? 'None'}
                </GridField>
              </FieldGrid>
            </section>
          ) : null
        }
      >
        <RunPayload module={module.id} runId={run.data.id} />
      </RunPanel>
    </>
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
      {!shown ? (
        <Button onPress={() => setShown(true)}>Show payload</Button>
      ) : payload.isPending ? (
        <p role="status">Loading the payload…</p>
      ) : payload.isError ? (
        <LoadError message={payload.error.message} onRetry={() => void payload.refetch()} />
      ) : (
        <>
          {payload.data.headers && Object.keys(payload.data.headers).length > 0 ? (
            <FieldGrid columns={2}>
              {Object.entries(payload.data.headers).map(([name, value]) => (
                <GridField key={name} label={name}>
                  <bdi dir="ltr">{value}</bdi>
                </GridField>
              ))}
            </FieldGrid>
          ) : null}
          <PayloadView content={payload.data.content} contentType={payload.data.contentType} />
        </>
      )}
    </section>
  )
}
