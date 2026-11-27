import { useModule } from '@headless/dashboard-sdk'
import { CommandAction, useAllowedActions, type RowResult } from '@headless/dashboard-sdk/commands'
import {
  Announcer,
  Banner,
  DetailPanel,
  EmptyState,
  FieldGrid,
  GridField,
  HistoryList,
  LoadError,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  StatCard,
  StatStrip,
  StatusMark,
  Tag,
  Text,
  Timestamp,
  usePageFrame,
  type HistoryItem,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { PingIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable } from '@headless/dashboard-sdk/table'
import { useState } from 'react'
import {
  checkFilters,
  healthMark,
  parseFilter,
  queryKeys,
  useCheckDetail,
  useChecks,
  useClusterCommands,
  useHostHealth,
  useSummary,
  type HealthCheck,
  type HostHealth,
  type ProbeResult,
} from '../api.ts'
import { CellText, FilterNote, StaleNotice, useKeptOpenRow, useLiveRefetch } from '../parts.tsx'

/** What a finished probe said, so a screen reader hears the result of a press whose rows change elsewhere. */
function useProbeAnnouncer() {
  const client = useQueryClient()
  const [said, setSaid] = useState({ text: '', round: 0 })

  function done(label: string, rows: readonly RowResult[]) {
    // The results live in the serving process, so the views read them again rather than wait for a change.
    void client.invalidateQueries({ queryKey: queryKeys.health })
    const text = rows.map((row) => `${row.id}: ${row.reason ?? row.outcome}`).join(', ')
    setSaid((current) => ({ text: `${label} ${text}`, round: current.round + 1 }))
  }

  return { said, done }
}

export default function HealthView() {
  const format = useFormat()
  const module = useModule()
  const summary = useSummary()
  const commands = useClusterCommands()
  const [params] = useSearchParams()
  const status = parseFilter(checkFilters, params.get('status'))
  const registration = summary.data?.health
  const canRead = registration?.monitor === true
  const health = useHostHealth(canRead)
  const checks = useChecks(status, canRead)
  const openName = params.get('open')
  // The panel's own read, shared through the cache: its 404 says the open check is gone, not merely filtered out.
  const openDetail = useCheckDetail(canRead ? (openName ?? undefined) : undefined)
  const kept = useKeptOpenRow(checks.data ?? [], openName, {
    isFiltered: status !== undefined,
    isGone: isNotFound(openDetail.error),
  })
  const frame = usePageFrame({ viewId: 'cluster.health', rows: kept.rows })
  const announcer = useProbeAnnouncer()
  const host = summary.data?.host
  const probeAll = commands.find('cluster.probe-all')
  const allowed = useAllowedActions('health', host ? [{ id: host }] : [])

  useLiveRefetch(['check'], [queryKeys.health])

  if (summary.data && !registration) {
    return (
      <PageFrame frame={frame} header={<PageHeader title="Health checks" />}>
        <EmptyState title="Health checks are not registered" placement="page">
          This host registers no health checks. Call services.AddHealthChecks() and add the host&apos;s checks to show
          them here.
        </EmptyState>
      </PageFrame>
    )
  }

  const total = health.data
    ? health.data.totals.healthy +
      health.data.totals.degraded +
      health.data.totals.unhealthy +
      health.data.totals.notProbed
    : undefined

  return (
    <PageFrame
      frame={frame}
      stats={health.data ? <HealthStats health={health.data} /> : undefined}
      header={
        <PageHeader
          title="Health checks"
          count={
            total === undefined ? undefined : health.data?.probedAt ? (
              <>
                {`${format.number(total)} checks · probed `}
                <Timestamp value={health.data.probedAt} />
              </>
            ) : (
              `${format.number(total)} checks · Not probed yet`
            )
          }
          description="This host only. Checks run when an operator probes them."
          actions={
            probeAll && host ? (
              <CommandAction
                module={module.id}
                command={probeAll}
                ids={[host]}
                noun="this host"
                icon={PingIcon}
                variant="primary"
                commands={commands.all}
                disabledReason={allowed(probeAll.id)}
                onDone={(rows) => announcer.done('Probe all', rows)}
              />
            ) : undefined
          }
        >
          {host ? <Tag>{`Host ${host}`}</Tag> : null}
        </PageHeader>
      }
      panel={
        frame.openId && canRead ? (
          <CheckPanel name={frame.openId} onProbed={(rows) => announcer.done('Probe now', rows)} />
        ) : undefined
      }
    >
      <StaleNotice
        what="these checks"
        onRefresh={() => void checks.refetch()}
        failed={checks.isError || health.isError || summary.isError}
      />
      <Announcer message={announcer.said.text} id={announcer.said.round} />
      {health.isError ? (
        <LoadError
          message={`The health summary did not load: ${health.error.message}`}
          kept={health.data !== undefined}
          onRetry={() => void health.refetch()}
        />
      ) : null}
      {registration && !registration.monitor ? (
        <Banner tone="warn">
          The health check monitor is not registered, so no check can be probed or shown. Call
          services.AddHeadlessHealthCheckMonitor(), or register the health checks before AddCluster().
        </Banner>
      ) : null}
      <QueryState query={summary} label="Loading health checks" rows={4} shape="table">
        {() =>
          canRead ? (
            <QueryState query={checks} label="Loading health checks" rows={4} shape="table">
              {() => (
                <DataTable
                  label="Health checks"
                  frame={frame}
                  rows={kept.rows}
                  emptyTitle={status ? 'No check matches this status' : 'No health checks'}
                  emptyState={
                    status
                      ? 'No check on this host has this latest result now.'
                      : 'A check appears here once the host registers one with AddHealthChecks().'
                  }
                  columns={[
                    {
                      id: 'check',
                      header: 'Check',
                      cell: (row) => (
                        <CellText title={row.name}>
                          <Mono>{row.name}</Mono>
                          {kept.isKept(row) ? <FilterNote /> : null}
                        </CellText>
                      ),
                      isRowHeader: true,
                      kind: 'id',
                      width: '1.5fr',
                    },
                    {
                      id: 'tags',
                      header: 'Tags',
                      cell: (row) => (
                        <CellText title={row.tags.join(', ')}>
                          {row.tags.map((tag) => (
                            <Tag key={tag}>{tag}</Tag>
                          ))}
                        </CellText>
                      ),
                      priority: 2,
                      width: '1.5fr',
                    },
                    {
                      id: 'status',
                      header: 'Status',
                      cell: (row) => (
                        <CellText>
                          {row.latest ? <StatusMark {...healthMark(row.latest.status)} /> : <NotProbed />}
                        </CellText>
                      ),
                      kind: 'status',
                      width: 130,
                    },
                    {
                      id: 'duration',
                      header: 'Duration',
                      cell: (row) => (row.latest ? format.duration(row.latest.durationMilliseconds) : ''),
                      kind: 'duration',
                      priority: 2,
                      width: 100,
                    },
                    {
                      id: 'probed',
                      header: 'Last probed',
                      cell: (row) => (row.latest ? <Timestamp value={row.latest.completedAt} /> : ''),
                      kind: 'time',
                      width: 120,
                    },
                    {
                      id: 'failureStatus',
                      header: 'Failure status',
                      cell: (row) => row.failureStatus,
                      priority: 2,
                      width: 120,
                    },
                  ]}
                />
              )}
            </QueryState>
          ) : null
        }
      </QueryState>
    </PageFrame>
  )
}

function isNotFound(error: unknown) {
  return error instanceof DashboardApiError && error.status === 404
}

function NotProbed() {
  return <Text tone="faint">Not probed</Text>
}

/** Overall, the totals, liveness, and the slowest probe; liveness and the slowest probe appear once a result exists. */
function HealthStats({ health }: { health: HostHealth }) {
  const format = useFormat()
  const { totals } = health
  const notProbed = totals.notProbed > 0 ? `${format.number(totals.notProbed)} not probed` : undefined

  return (
    <StatStrip label="Health at a glance">
      {health.status ? (
        <StatCard
          label="Overall"
          tone={healthMark(health.status).tone}
          value={health.status}
          caption={notProbed}
          to={notProbed ? '?status=NotProbed' : undefined}
        />
      ) : (
        <StatCard label="Overall" value="Not probed" caption={notProbed} to="?status=NotProbed" />
      )}
      <StatCard label="Healthy" tone="ok" value={format.number(totals.healthy)} to="?status=Healthy" />
      <StatCard label="Degraded" tone="proc" value={format.number(totals.degraded)} to="?status=Degraded" />
      <StatCard label="Unhealthy" tone="fail" value={format.number(totals.unhealthy)} to="?status=Unhealthy" />
      {health.probedAt && health.liveness ? (
        <StatCard
          label="Liveness"
          value={`${format.number(health.liveness.healthy)} / ${format.number(health.liveness.total)}`}
        />
      ) : null}
      {health.slowest ? (
        <StatCard
          label="Slowest probe"
          value={format.duration(health.slowest.durationMilliseconds)}
          caption={health.slowest.name}
        />
      ) : null}
    </StatStrip>
  )
}

/** `Healthy for 22 probes, then Degraded for the last 8`, from results newest first. */
function historySummary(results: readonly ProbeResult[]) {
  const runs: { status: string; count: number }[] = []

  for (const result of [...results].reverse()) {
    const last = runs.at(-1)

    if (last?.status === result.status) {
      last.count += 1
    } else {
      runs.push({ status: result.status, count: 1 })
    }
  }

  const probes = (count: number) => (count === 1 ? '1 probe' : `${count} probes`)
  const latest = runs.at(-1)
  const before = runs.at(-2)

  if (!latest) {
    return undefined
  }

  if (!before) {
    return `${latest.status} for ${results.length === 1 ? 'the only probe' : `all ${probes(latest.count)}`}`
  }

  return `${before.status} for ${probes(before.count)}, then ${latest.status} for the last ${latest.count === 1 ? 'probe' : latest.count}`
}

function resultItems(results: readonly ProbeResult[]): HistoryItem[] {
  return results.map((result) => ({
    key: result.completedAt,
    time: <Timestamp value={result.completedAt} />,
    tone: healthMark(result.status).tone,
    state: result.status,
    note: result.description ?? undefined,
  }))
}

function CheckPanel({ name, onProbed }: { name: string; onProbed: (rows: RowResult[]) => void }) {
  const module = useModule()
  const format = useFormat()
  const commands = useClusterCommands()
  const detail = useCheckDetail(name)
  const probe = commands.find('cluster.probe')
  const allowed = useAllowedActions('check', [{ id: name }])
  const gone = isNotFound(detail.error)

  if (gone) {
    return (
      <DetailPanel label={`Check ${name}`} name={name}>
        <EmptyState role="alert" title="This host no longer registers a check with this name." />
      </DetailPanel>
    )
  }

  const check: HealthCheck | undefined = detail.data?.check
  const results = detail.data?.results ?? []
  const summary = historySummary(results)

  return (
    <DetailPanel
      label={`Check ${name}`}
      name={name}
      status={check?.latest ? healthMark(check.latest.status) : undefined}
      actions={
        probe ? (
          <CommandAction
            module={module.id}
            command={probe}
            ids={[check?.name ?? name]}
            noun={`check ${name}`}
            icon={PingIcon}
            variant="primary"
            commands={commands.all}
            disabledReason={allowed(probe.id)}
            onDone={onProbed}
          />
        ) : undefined
      }
      tabs={[
        {
          id: 'overview',
          label: 'Overview',
          content: (
            <QueryState query={detail} label="Loading the check" rows={4} shape="fields">
              {(data) => (
                <FieldGrid label="Check">
                  <GridField label="Status">
                    {data.check.latest ? <StatusMark {...healthMark(data.check.latest.status)} /> : <NotProbed />}
                  </GridField>
                  <GridField label="Failure status">{data.check.failureStatus}</GridField>
                  <GridField label="Timeout">
                    {data.check.timeoutMilliseconds === null ? 'None' : format.duration(data.check.timeoutMilliseconds)}
                  </GridField>
                  <GridField label="Liveness">{data.check.isLiveness ? 'Yes' : 'No'}</GridField>
                  <GridField label="Tags" span={2}>
                    {data.check.tags.length > 0 ? data.check.tags.join(', ') : 'None'}
                  </GridField>
                  <GridField label="Latest description" span={2}>
                    {data.check.latest?.description ?? (data.check.latest ? 'None' : 'Not probed yet')}
                  </GridField>
                </FieldGrid>
              )}
            </QueryState>
          ),
        },
        {
          id: 'history',
          label: 'History',
          content:
            results.length > 0 ? (
              <>
                {summary ? (
                  <Text as="p" tone="hint">
                    {summary}
                  </Text>
                ) : null}
                <HistoryList label="Recent probes" items={resultItems(results)} />
              </>
            ) : null,
        },
      ]}
    />
  )
}
