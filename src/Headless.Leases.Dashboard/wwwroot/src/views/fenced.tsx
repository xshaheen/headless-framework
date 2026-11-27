import { useDashboard } from '@headless/dashboard-sdk'
import {
  Banner,
  Button,
  DetailPanel,
  EmptyState,
  FieldGrid,
  FilterSelect,
  FilterToolbar,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  SegmentedControl,
  StatCard,
  StatStrip,
  StatusMark,
  Text,
  Timestamp,
  usePageFrame,
  GridField,
} from '@headless/dashboard-sdk/components'
import { useDashboardKeysetList, type CappedCount } from '@headless/dashboard-sdk/data'
import { ListIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { Navigate, useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable, SearchField } from '@headless/dashboard-sdk/table'
import {
  fencedTopic,
  fetchLeases,
  leaseMark,
  pageReadAt,
  queryKeys,
  readFilter,
  stateFilters,
  useCounts,
  useKinds,
  useLease,
  useSummary,
  type FencedCounts,
  type FencedLease,
  type StateFilter,
} from '../api.ts'
import { CellText, isNotFound, LeaseTypeSwitch, ReadAt } from '../parts.tsx'

const scopeOptions = [
  { value: 'host', label: 'Host' },
  { value: 'tenant', label: 'One tenant' },
] as const

export default function FencedLeasesView() {
  const { api } = useDashboard()
  const format = useFormat()
  const client = useQueryClient()
  const summary = useSummary()
  const [params, setParams] = useSearchParams()
  const filter = readFilter(params)
  const prefix = params.get('prefix') ?? ''
  const hasFencing = summary.data?.fencing != null
  const counts = useCounts(filter, prefix, hasFencing)
  const kinds = useKinds(hasFencing)
  const leases = useDashboardKeysetList<FencedLease>({
    topic: fencedTopic,
    params: { ...filter, enabled: hasFencing },
    search: prefix,
    fetchPage: (cursor) =>
      hasFencing
        ? fetchLeases(api, filter, prefix, cursor)
        : Promise.resolve({ items: [], nextCursor: null, readAt: '' }),
  })
  const frame = usePageFrame({ viewId: 'leases.fenced', rows: leases.rows })
  const openId = frame.openId
  const firstPage = leases.query.data?.pages[0]
  const isFiltered = filter.state !== 'All' || filter.kind !== '' || filter.scope !== 'any' || prefix !== ''

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

  function set(name: string, value: string) {
    update((next) => (value ? next.set(name, value) : next.delete(name)))
  }

  function setScope(value: string) {
    update((next) => {
      next.delete('scope')
      next.delete('tenant')

      if (value === 'host') {
        next.set('scope', 'host')
      } else if (value === 'tenant') {
        // An empty tenant marks the choice; nothing is sent until the field holds an id.
        next.set('tenant', '')
      }
    })
  }

  function clearFilters() {
    update((next) => {
      for (const name of ['state', 'kind', 'scope', 'tenant', 'prefix']) {
        next.delete(name)
      }
    })
  }

  function refresh() {
    void leases.query.refetch()
    void counts.refetch()

    if (openId) {
      void client.invalidateQueries({ queryKey: queryKeys.lease(openId) })
    }
  }

  if (summary.data && !summary.data.fencing) {
    // The sidebar entry lands here, so a lock-only host goes straight to the half it serves.
    if (summary.data.locks) {
      return <Navigate to="/leases/locks" replace />
    }

    return (
      <PageFrame frame={frame} header={<PageHeader title="Fenced leases" />}>
        <EmptyState icon={ListIcon} title="Fencing is not registered" placement="page">
          This host serves no fenced leases. Call services.AddHeadlessFencing(...) with a provider to show them here.
        </EmptyState>
      </PageFrame>
    )
  }

  return (
    <PageFrame
      frame={frame}
      stats={<LeaseStats counts={counts.data} kinds={summary.data?.fencing?.kinds} />}
      header={
        <PageHeader
          title="Fenced leases"
          count={counts.data ? format.count(counts.data.total) : undefined}
          description="Read-only. Lease times come from the database clock, never this browser's."
          toolbar={
            <FilterToolbar
              active={[filter.kind, filter.scope !== 'any' ? filter.scope : ''].filter(Boolean).length}
              search={
                <SearchField
                  key={`prefix-${prefix}`}
                  label="Resource prefix"
                  placeholder="Resource starts with (literal, case-sensitive)"
                  compact
                  defaultValue={prefix}
                  onSearch={(text) => set('prefix', text)}
                />
              }
            >
              <SegmentedControl<StateFilter>
                label="State"
                options={stateFilters.map((state) => ({
                  value: state,
                  label: stateLabel(state, counts.data, format.count),
                }))}
                value={filter.state}
                onChange={(state) => set('state', state === 'All' ? '' : state)}
              />
              <FilterSelect
                label="Kind"
                emptyLabel="Any kind"
                value={filter.kind}
                onChange={(value) => set('kind', value)}
                options={(kinds.data ?? []).map((kind) => ({ value: kind, label: kind }))}
              />
              <FilterSelect
                label="Tenant"
                emptyLabel="Any tenant"
                value={filter.scope === 'any' ? '' : filter.scope}
                onChange={setScope}
                options={scopeOptions}
              />
              {filter.scope === 'tenant' ? (
                <SearchField
                  key={`tenant-${filter.tenant}`}
                  label="Tenant id"
                  placeholder="Tenant id, then Enter"
                  mode="submit"
                  compact
                  defaultValue={filter.tenant}
                  onSearch={(text) => update((next) => next.set('tenant', text))}
                />
              ) : null}
            </FilterToolbar>
          }
          actions={
            <>
              <LeaseTypeSwitch current="fenced" summary={summary.data} />
              <ReadAt at={pageReadAt(firstPage)} isRefreshing={leases.query.isFetching} onRefresh={refresh} />
            </>
          }
        />
      }
      panel={openId ? <LeasePanel id={openId} /> : undefined}
    >
      <QueryState query={summary} label="Loading leases" rows={6} shape="table">
        {() => (
          <QueryState query={leases.query} label="Loading leases" rows={6} shape="table">
            {() => (
              <DataTable
                label="Fenced leases"
                frame={frame}
                rows={leases.rows}
                hasMore={leases.hasMore}
                isLoadingMore={leases.isLoadingMore}
                onLoadMore={leases.loadMore}
                emptyIcon={ListIcon}
                emptyTitle={isFiltered ? 'No leases match' : 'No fenced leases'}
                emptyState={
                  isFiltered
                    ? 'No lease in the store matches these filters.'
                    : 'A lease appears here once code calls IFencedLeases.GrantAsync.'
                }
                emptyAction={isFiltered ? <Button onPress={clearFilters}>Clear filters</Button> : undefined}
                columns={[
                  {
                    id: 'resource',
                    header: 'resource',
                    cell: (row) => (
                      <CellText title={row.resource}>
                        <Mono>{row.resource}</Mono>
                      </CellText>
                    ),
                    isRowHeader: true,
                    kind: 'id',
                    width: '2fr',
                  },
                  {
                    id: 'state',
                    header: 'state',
                    cell: (row) => (
                      <CellText>
                        <StatusMark {...leaseMark(row.status)} />
                      </CellText>
                    ),
                    kind: 'status',
                    width: 130,
                  },
                  {
                    id: 'kind',
                    header: 'kind',
                    cell: (row) => (
                      <CellText title={row.kind}>
                        <Mono>{row.kind}</Mono>
                      </CellText>
                    ),
                    width: '1fr',
                  },
                  {
                    id: 'tenant',
                    header: 'tenant',
                    cell: (row) => <CellText title={row.tenantId ?? 'Host'}>{tenantText(row)}</CellText>,
                    priority: 2,
                    width: '1fr',
                  },
                  {
                    id: 'generation',
                    header: 'generation',
                    cell: (row) => <CellText>{format.number(row.generation)}</CellText>,
                    kind: 'number',
                    align: 'end',
                    priority: 2,
                    width: 110,
                  },
                  {
                    id: 'granted',
                    header: 'granted',
                    cell: (row) => (
                      <CellText>
                        <Timestamp value={row.grantedAt} />
                      </CellText>
                    ),
                    kind: 'time',
                    priority: 2,
                    width: 130,
                  },
                  {
                    id: 'expires',
                    header: 'expires',
                    cell: (row) => (
                      <CellText>
                        <Timestamp value={row.expiresAt} />
                      </CellText>
                    ),
                    kind: 'time',
                    width: 130,
                  },
                  {
                    id: 'takeovers',
                    header: 'takeovers',
                    cell: (row) => <CellText>{format.number(row.takeoverCount)}</CellText>,
                    kind: 'number',
                    align: 'end',
                    priority: 2,
                    width: 100,
                  },
                  {
                    id: 'progress',
                    header: 'progress',
                    cell: (row) => <CellText>{progressText(row, format.size)}</CellText>,
                    priority: 2,
                    width: '1fr',
                  },
                ]}
              />
            )}
          </QueryState>
        )}
      </QueryState>
    </PageFrame>
  )
}

/** The served counts only: live, expired, and ended leases for the filter, and the kinds in use. */
function LeaseStats({ counts, kinds }: { counts: FencedCounts | undefined; kinds: number | undefined }) {
  const format = useFormat()
  const value = (count: CappedCount | undefined) => (count ? format.count(count) : '–')

  return (
    <StatStrip label="Leases at a glance">
      <StatCard label="Live" tone="ok" value={value(counts?.live)} to="?state=Live" />
      <StatCard label="Expired" tone="fail" value={value(counts?.expired)} to="?state=Expired" />
      <StatCard label="Ended" tone="off" value={value(counts?.ended)} to="?state=Ended" />
      <StatCard label="Kinds" value={kinds === undefined ? '–' : format.number(kinds)} />
    </StatStrip>
  )
}

function stateLabel(state: StateFilter, counts: FencedCounts | undefined, count: (value: CappedCount) => string) {
  if (!counts) {
    return state
  }

  const figure = { All: counts.total, Live: counts.live, Expired: counts.expired, Ended: counts.ended }[state]

  return `${state} ${count(figure)}`
}

function tenantText(lease: FencedLease) {
  return lease.tenantId ?? 'Host'
}

/** `1.2 kB · export-cursor/v1`: the size and contract only, since the payload is the holder's resume data. */
function progressText(lease: FencedLease, size: (bytes: number) => string) {
  if (lease.progressSizeBytes == null) {
    return '—'
  }

  return lease.progressContract
    ? `${size(lease.progressSizeBytes)} · ${lease.progressContract}`
    : size(lease.progressSizeBytes)
}

function LeasePanel({ id }: { id: string }) {
  const format = useFormat()
  const detail = useLease(id)

  if (isNotFound(detail.error)) {
    return (
      <DetailPanel label={`Lease ${id}`} name={id}>
        <Banner tone="warn">
          This lease has no row any more: a purge removed it, so there is nothing left to show.
        </Banner>
      </DetailPanel>
    )
  }

  const lease = detail.data?.lease

  return (
    <DetailPanel
      label={`Lease ${lease?.resource ?? id}`}
      name={lease?.resource ?? id}
      status={lease ? leaseMark(lease.status) : undefined}
    >
      <QueryState query={detail} label="Loading the lease" rows={6} shape="fields">
        {({ lease }) => (
          <>
            <FieldGrid label="Lease" footnote="Times come from the database clock.">
              <GridField label="State">
                <StatusMark {...leaseMark(lease.status)} />
              </GridField>
              <GridField label="Generation">{format.number(lease.generation)}</GridField>
              <GridField label="Takeovers">{format.number(lease.takeoverCount)}</GridField>
              <GridField label="Tenant">{tenantText(lease)}</GridField>
              <GridField label="Kind">
                <Mono>{lease.kind}</Mono>
              </GridField>
              <GridField label="Resource" span={2}>
                <Mono>{lease.resource}</Mono>
              </GridField>
              <GridField label="Holder" span={2}>
                {lease.holder ? <Mono>{lease.holder}</Mono> : 'No holder recorded'}
              </GridField>
              <GridField label="Granted">
                <Timestamp value={lease.grantedAt} />
              </GridField>
              <GridField label="Expires">
                <Timestamp value={lease.expiresAt} />
              </GridField>
              <GridField label="Ended">
                <Timestamp value={lease.endedAt} missing="Not ended" />
              </GridField>
              <GridField label="Progress">{progressText(lease, format.size)}</GridField>
            </FieldGrid>
            <Text as="h3" tone="label">
              Why there is no Release action
            </Text>
            <Text as="p" tone="hint">
              A release here cannot stop what the holder is already doing outside the store: it would only let a second
              worker start the same work while the first finishes. The holder releases or settles its lease from code,
              and an expired lease ends through a takeover or a sweep. Purge ended leases from code with
              IFencedLeases.PurgeAsync.
            </Text>
          </>
        )}
      </QueryState>
    </DetailPanel>
  )
}
