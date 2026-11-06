import { useDashboard, useModule } from '@headless/dashboard-sdk'
import {
  Button,
  DetailPanel,
  FieldGrid,
  GridField,
  HistoryList,
  Mono,
  PageFrame,
  PageHeader,
  Tag,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList, useDashboardList } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { Link, useNavigate } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { cronJobsTopic, cronStatusMark, fetchCronJobs, type CronJob, type JobFunction } from '../cron.ts'
import { CellText, JobsStats, LoadError, QueryState, Skeleton, StaleNotice, Time, useFunctions } from '../parts.tsx'
import { fetchRuns, runStatusMark, runsTopic, statusLabel, viewOnlyReason, type Run } from '../runs.ts'

/** A function declared with a cron expression is fired by its schedule; any other is added as a time job. */
function FunctionKind({ item }: { item: Pick<JobFunction, 'cronExpression'> }) {
  return item.cronExpression ? (
    <>
      <Tag variant="filled">cron</Tag> <Mono>{item.cronExpression}</Mono>
    </>
  ) : (
    <Tag variant="filled">time</Tag>
  )
}

/** The live nodes whose run filter claims the function, or why none runs it: its runs wait until one does. */
function RunsOn({ item }: { item: Pick<JobFunction, 'claimedBy'> }) {
  return item.claimedBy.length > 0 ? (
    <>
      {item.claimedBy.map((node) => (
        <Tag key={node} variant="square">
          {node}
        </Tag>
      ))}
    </>
  ) : (
    <Tag tone="fail" title="No live node's run filter claims it, so its runs wait.">
      no node runs it
    </Tag>
  )
}

function useLimit() {
  const format = useFormat()

  return (item: Pick<JobFunction, 'running' | 'maxConcurrency'>) =>
    `${format.number(item.running)} / ${item.maxConcurrency > 0 ? format.number(item.maxConcurrency) : 'no limit'}`
}

export default function FunctionsView() {
  const format = useFormat()
  const limit = useLimit()
  const functions = useFunctions()
  const rows = (functions.data ?? []).map((item) => ({ ...item, id: item.name }))
  const frame = usePageFrame({ viewId: 'jobs.functions', rows })
  const open = rows.find((row) => row.id === frame.openId)
  const unclaimed = rows.filter((row) => row.claimedBy.length === 0).length

  return (
    <PageFrame
      frame={frame}
      stats={<JobsStats />}
      header={
        <PageHeader
          title="Functions"
          count={functions.data ? format.number(rows.length) : undefined}
          description={
            unclaimed > 0
              ? `${format.number(unclaimed)} not run by any live node; their runs wait until a node claims them.`
              : 'Every function the live nodes register, with what runs it and its last 24 hours.'
          }
        />
      }
      panel={open ? <FunctionPanel item={open} /> : undefined}
    >
      <StaleNotice what="these functions" onRefresh={() => void functions.refetch()} />
      <QueryState query={functions} label="Loading functions" rows={4}>
        {() => (
          <DataTable
            label="Functions"
            viewId="jobs.functions"
            frame={frame}
            rows={rows}
            emptyState="No functions registered on any live node."
            columns={[
              {
                id: 'name',
                header: 'Function',
                cell: (row) => (
                  <CellText title={row.name}>
                    <Mono>{row.name}</Mono>
                  </CellText>
                ),
                isRowHeader: true,
                width: '1.6fr',
              },
              {
                id: 'kind',
                header: 'Kind',
                cell: (row) => (
                  <CellText title={row.cronExpression}>
                    <FunctionKind item={row} />
                  </CellText>
                ),
                width: '1.2fr',
              },
              {
                id: 'request',
                header: 'Request type',
                cell: (row) => (
                  <CellText title={row.requestType}>
                    <Mono>{row.requestType ?? '–'}</Mono>
                  </CellText>
                ),
                priority: 2,
                width: '1.4fr',
              },
              { id: 'priority', header: 'Priority', cell: (row) => row.priority, priority: 2, width: 100 },
              { id: 'limit', header: 'Running / limit', cell: (row) => limit(row), align: 'end', priority: 2, width: 120 },
              {
                id: 'runs-on',
                header: 'Runs on',
                cell: (row) => (
                  <CellText title={row.claimedBy.join(', ')}>
                    <RunsOn item={row} />
                  </CellText>
                ),
                width: '1.3fr',
              },
              { id: 'succeeded', header: 'Succeeded, 24 h', cell: (row) => format.number(row.succeeded), align: 'end', priority: 2, width: 120 },
              { id: 'failed', header: 'Failed, 24 h', cell: (row) => format.number(row.failed), align: 'end', width: 100 },
            ]}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

function FunctionFields({ item }: { item: JobFunction }) {
  const format = useFormat()
  const limit = useLimit()

  return (
    <FieldGrid>
      <GridField label="Contract version">
        <Mono>{item.contractVersion}</Mono>
      </GridField>
      <GridField label="Priority">{item.priority}</GridField>
      <GridField label="Running / limit">{limit(item)}</GridField>
      <GridField label="Kind">
        <FunctionKind item={item} />
      </GridField>
      <GridField label="Request type" span={2}>
        {item.requestType ? <Mono>{item.requestType}</Mono> : 'None'}
      </GridField>
      <GridField label="Last run" span={2}>
        <Time value={item.lastExecutedAt} />
      </GridField>
      <GridField label="Succeeded, 24 h">{format.number(item.succeeded)}</GridField>
      <GridField label="Failed, 24 h">{format.number(item.failed)}</GridField>
      <GridField label="Waiting">{format.number(item.waiting)}</GridField>
    </FieldGrid>
  )
}

function WhereItRuns({ item }: { item: JobFunction }) {
  const { api } = useDashboard()
  const filter = { function: item.name }
  const cronJobs = useDashboardList<CronJob>({ topic: cronJobsTopic, params: filter, fetch: () => fetchCronJobs(api, item.name) })
  const listed = cronJobs.data?.items ?? []

  return (
    <div>
      <section aria-label="Where it runs" className="hd-fields">
        <h3 className="hd-label">Where it runs</h3>
        {item.claimedBy.length > 0 ? (
          <div>
            {item.claimedBy.map((node) => (
              <Tag key={node} variant="square">
                <Link to={`/jobs/nodes?open=${encodeURIComponent(node)}`}>{node}</Link>
              </Tag>
            ))}
          </div>
        ) : (
          <p>
            <Tag tone="fail">no node runs it</Tag> No live node's run filter claims it, so its runs wait until one does.
          </p>
        )}
      </section>
      {cronJobs.isError ? (
        <LoadError message={cronJobs.error.message} onRetry={() => void cronJobs.refetch()} />
      ) : cronJobs.data && listed.length === 0 ? (
        <section aria-label="Recurring jobs using it" className="hd-fields">
          <h3 className="hd-label">Recurring jobs using it</h3>
          <span className="hd-hint">None. Add one to run this function on a schedule.</span>
        </section>
      ) : (
        <HistoryList
          label="Recurring jobs using it"
          items={listed.map((cronJob) => ({
            key: cronJob.id,
            time: <Mono>{cronJob.expression}</Mono>,
            tone: cronStatusMark(cronJob).tone,
            state: cronJob.reading ?? cronStatusMark(cronJob).label,
            note: cronJob.isPaused ? 'paused' : undefined,
            actions: <Link to={`/jobs/cron-jobs?open=${encodeURIComponent(cronJob.id)}`}>Open</Link>,
          }))}
        />
      )}
    </div>
  )
}

function FunctionRuns({ name }: { name: string }) {
  const { api } = useDashboard()
  const filter = { function: name }
  const runs = useDashboardKeysetList<Run>({ topic: runsTopic, params: filter, fetchPage: (cursor) => fetchRuns(api, filter, cursor) })

  if (runs.query.isError) {
    return <LoadError message={runs.query.error.message} onRetry={() => void runs.query.refetch()} />
  }

  if (runs.query.isPending) {
    return <Skeleton label="Loading the runs" rows={3} />
  }

  return (
    <div>
      {runs.rows.length === 0 ? (
        <p>No runs of this function yet.</p>
      ) : (
        <HistoryList
          label="Latest runs"
          items={runs.rows.map((run) => ({
            key: run.id,
            time: <Time value={run.executionTime ?? run.createdAt} short />,
            tone: runStatusMark(run).tone,
            state: statusLabel(run.status),
            note: run.node,
            actions: <Link to={`/jobs/runs/${encodeURIComponent(run.id)}`}>Open run</Link>,
          }))}
        />
      )}
      <Link to={`/jobs/runs?function=${encodeURIComponent(name)}`}>All runs of this function</Link>
    </div>
  )
}

export function FunctionPanel({ item, page }: { item: JobFunction; page?: boolean }) {
  const module = useModule()
  const navigate = useNavigate()
  const addReason = module.canOperate ? null : viewOnlyReason

  return (
    <DetailPanel
      label={`Function ${item.name}`}
      name={item.name}
      status={item.claimedBy.length > 0 ? undefined : { tone: 'fail', label: 'No node runs it' }}
      detachTo={page ? undefined : `/jobs/functions/${encodeURIComponent(item.name)}`}
      actions={
        <>
          <Button variant="primary" disabledReason={addReason} onPress={() => void navigate('/jobs/runs?create=time-job')}>
            Add time job
          </Button>
          <Button disabledReason={addReason} onPress={() => void navigate('/jobs/cron-jobs?create=cron-job')}>
            Add recurring job
          </Button>
        </>
      }
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <>
              <div>
                <FunctionFields item={item} />
              </div>
              <WhereItRuns item={item} />
            </>
          ),
        },
        { id: 'runs', label: 'Runs', content: <FunctionRuns name={item.name} /> },
      ]}
    />
  )
}
