import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { CommandAction, useOperations } from '@headless/dashboard-sdk/commands'
import {
  Banner,
  Button,
  DetailPanel,
  EmptyState,
  FilterToolbar,
  LoadError,
  PageFrame,
  PageHeader,
  QueryState,
  Skeleton,
  usePageFrame,
  type DetailTab,
} from '@headless/dashboard-sdk/components'
import {
  apiUrl,
  DashboardApiError,
  refetchTopics,
  useDashboardKeysetList,
  useDashboardObject,
  useNewRows,
} from '@headless/dashboard-sdk/data'
import { DeleteIcon, ReceivedIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQueryClient } from '@headless/dashboard-sdk/query'
import { useSearchParams } from '@headless/dashboard-sdk/router'
import { DataTable, SearchField, type DataTableColumn } from '@headless/dashboard-sdk/table'
import type { ReactNode } from 'react'
import {
  channelNouns,
  fetchDetail,
  fetchPage,
  topicOf,
  useChannelCommands,
  useChannels,
  viewOnlyReason,
  type CaptureRow,
  type ChannelKind,
} from './inbox.ts'

/** The note every view opens with: what the inbox holds, and that nothing it shows was delivered. */
export function DevelopmentBanner() {
  return (
    <Banner tone="info">Development only. These messages were captured instead of sent; nothing was delivered.</Banner>
  )
}

/** A failed send's error, above the panel's tabs, in the failure tone. */
export function FailureNotice({ error }: { error?: string | null }) {
  return error ? <Banner tone="error">{`The send failed: ${error}`}</Banner> : null
}

/** Puts a failed send's error at the top of every tab the panel shows, so it reads first whichever tab is open. */
export function withFailure(tabs: readonly DetailTab[], error?: string | null): DetailTab[] {
  return tabs.map((tab) =>
    tab.content === null || !error
      ? tab
      : {
          ...tab,
          content: (
            <>
              <FailureNotice error={error} />
              {tab.content}
            </>
          ),
        },
  )
}

/** A failed send reads as failed; an accepted capture carries no state. */
export function captureStatus(row: Pick<CaptureRow, 'failed'>) {
  return row.failed ? ({ tone: 'fail', label: 'Failed' } as const) : undefined
}

/** A download link to a module endpoint, which answers as an attachment. */
export function DownloadLink({ path, children }: { path: string; children: ReactNode }) {
  const { apiBase } = useDashboard()

  return (
    <a href={apiUrl(apiBase, path)} download>
      {children}
    </a>
  )
}

/** Delete one capture: an irreversible change, confirmed with what it removes, and refused to a viewer. */
export function DeleteAction({ kind, id, name }: { kind: ChannelKind; id: string; name: string }) {
  const module = useModule()
  const commands = useChannelCommands(kind)
  const client = useQueryClient()
  const command = commands.delete

  return command ? (
    <CommandAction
      module={module.id}
      command={command}
      ids={[id]}
      noun={channelNouns[kind].one}
      variant="danger"
      icon={DeleteIcon}
      commands={commands.all}
      reach={`${name} is removed from the inbox for good.`}
      disabledReason={command.canRun ? null : (command.disabledReason ?? viewOnlyReason)}
      onDone={() => void refetchTopics(client, [topicOf(kind)])}
    />
  ) : null
}

/** Clear a channel, or the captures its search matches: an Operation the operator gives a reason for. */
function ClearAction({ kind, search }: { kind: ChannelKind; search: string }) {
  const module = useModule()
  const { start } = useOperations()
  const commands = useChannelCommands(kind)
  const command = commands.clear
  const nouns = channelNouns[kind]

  return command ? (
    <Button
      variant="danger"
      disabledReason={command.canRun ? null : (command.disabledReason ?? viewOnlyReason)}
      onPress={() =>
        start({
          module: module.id,
          command,
          filter: { search: search || null },
          noun: search ? `captured ${nouns.many} that match the search` : `captured ${nouns.many}`,
        })
      }
    >
      {search ? 'Clear matching' : 'Clear'}
    </Button>
  ) : null
}

export interface ChannelViewProps<TRow extends CaptureRow, TDetail extends CaptureRow> {
  kind: ChannelKind
  title: string
  description: string
  columns: readonly DataTableColumn<TRow>[]
  /** The open capture's panel, once its detail answered. */
  panel: (detail: TDetail) => ReactNode
}

/**
 * One channel's list, search, live new rows, Clear, and the docked panel of the open capture. A channel whose capture
 * store the host did not register names the provider call it needs instead of an empty list.
 */
export function ChannelView<TRow extends CaptureRow, TDetail extends CaptureRow>(
  props: ChannelViewProps<TRow, TDetail>,
) {
  const channels = useChannels()
  const nouns = channelNouns[props.kind]

  if (channels.data && !channels.data.some((channel) => channel.kind === props.kind)) {
    return (
      <UnconfiguredChannel title={props.title}>
        <EmptyState icon={ReceivedIcon} title={`This host captures no ${nouns.many}.`} placement="page">
          {`Call UseDevelopment() in ${nouns.setup} to capture them here.`}
        </EmptyState>
      </UnconfiguredChannel>
    )
  }

  if (!channels.data) {
    return (
      <UnconfiguredChannel title={props.title}>
        <QueryState query={channels} label={`Loading ${nouns.many}`} rows={6} shape="table">
          {() => null}
        </QueryState>
      </UnconfiguredChannel>
    )
  }

  return <ConfiguredChannel {...props} capacity={channels.data.find((c) => c.kind === props.kind)?.capacity} />
}

function UnconfiguredChannel({ title, children }: { title: string; children: ReactNode }) {
  const frame = usePageFrame({ viewId: `dev-inbox.${title}`, rows: [] })

  return (
    <PageFrame frame={frame} header={<PageHeader title={title} />}>
      <DevelopmentBanner />
      {children}
    </PageFrame>
  )
}

function ConfiguredChannel<TRow extends CaptureRow, TDetail extends CaptureRow>({
  kind,
  title,
  description,
  columns,
  panel,
  capacity,
}: ChannelViewProps<TRow, TDetail> & { capacity?: number }) {
  const { api } = useDashboard()
  const format = useFormat()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const topic = topicOf(kind)
  const nouns = channelNouns[kind]
  const list = useDashboardKeysetList<TRow>({
    topic,
    search,
    fetchPage: (cursor, request) => fetchPage<TRow>(api, kind, cursor, request),
  })
  // A row carries no body, so the browser cannot tell whether a new capture matches a search: while one is set, the
  // list keeps to the rows the server matched.
  const searched = search
    ? new Set(list.query.data?.pages.flatMap((page) => page.items.map((row) => row.id)))
    : undefined
  const matches = (row: TRow) => !searched || searched.has(row.id)
  const frame = usePageFrame({ viewId: `dev-inbox.${kind}`, rows: list.rows, matches })
  const newRows = useNewRows<TRow>(topic, matches)

  function setSearch(text: string) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current)

        if (text) {
          next.set('search', text)
        } else {
          next.delete('search')
        }

        return next
      },
      { replace: true },
    )
  }

  return (
    <PageFrame
      frame={frame}
      header={
        <PageHeader
          title={title}
          count={list.query.data ? format.number(list.rows.length) + (list.hasMore ? '+' : '') : undefined}
          description={
            capacity
              ? `${description} Kept in memory, newest ${format.number(capacity)}. Cleared when the app restarts.`
              : description
          }
          actions={<ClearAction kind={kind} search={search} />}
          toolbar={
            <FilterToolbar
              active={search ? 1 : 0}
              search={
                <SearchField
                  label={`Search ${nouns.many}`}
                  placeholder={`Search ${nouns.many}`}
                  compact
                  defaultValue={search}
                  onSearch={setSearch}
                />
              }
            />
          }
        />
      }
      panel={frame.openId ? <OpenCapture key={frame.openId} kind={kind} id={frame.openId} panel={panel} /> : undefined}
    >
      <DevelopmentBanner />
      <QueryState query={list.query} label={`Loading ${nouns.many}`} rows={6} shape="table">
        {() => (
          <DataTable
            label={title}
            frame={frame}
            rows={list.rows}
            matches={matches}
            hasMore={list.hasMore}
            isLoadingMore={list.isLoadingMore}
            onLoadMore={list.loadMore}
            newRows={newRows}
            emptyIcon={ReceivedIcon}
            emptyTitle={search ? `No ${nouns.many} match the search` : `No ${nouns.many} captured yet`}
            emptyState={search ? 'Try other words, or clear the search.' : `Send one from the app.`}
            columns={columns}
          />
        )}
      </QueryState>
    </PageFrame>
  )
}

/** The open capture, read through the API first, so a gone capture says so instead of a frame that cannot tell. */
function OpenCapture<TDetail extends CaptureRow>({
  kind,
  id,
  panel,
}: {
  kind: ChannelKind
  id: string
  panel: (detail: TDetail) => ReactNode
}) {
  const { api } = useDashboard()
  const nouns = channelNouns[kind]
  const detail = useDashboardObject({ topic: topicOf(kind), id, fetch: () => fetchDetail<TDetail>(api, kind, id) })

  // A read that fails keeps the last data it had, so a capture deleted while open reads as gone, not as still there.
  const isGone = detail.isError && detail.error instanceof DashboardApiError && detail.error.status === 404

  if (detail.data && !isGone) {
    return panel(detail.data)
  }

  return (
    <DetailPanel label={`${nouns.one} ${id}`} name={nouns.one} id={id}>
      {detail.isError ? (
        isGone ? (
          <EmptyState role="alert" title={`This ${nouns.one} no longer exists.`} />
        ) : (
          <LoadError message={detail.error.message} onRetry={() => void detail.refetch()} />
        )
      ) : (
        <Skeleton label={`Loading the ${nouns.one}`} shape="fields" rows={4} />
      )}
    </DetailPanel>
  )
}
