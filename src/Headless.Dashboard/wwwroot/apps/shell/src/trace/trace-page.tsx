// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import {
  Button,
  CopyButton,
  EmptyState,
  Kbd,
  Mono,
  PageHeader,
  QueryState,
  Timeline,
  Timestamp,
  usePageTitle,
  type TimelineEvent,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { Link, useLocation } from '@headless/dashboard-sdk/router'
import { modulePath } from '../route-handles.ts'
import { useOpenObject } from '../shell-context.tsx'
import { useTrace, type Trace, type TraceSection } from '../shell-data.ts'

/** The longest correlation id the server traces; a longer one is refused, so the page says why instead of asking. */
const maxIdLength = 200

/** The server's tag codes in words; a code the shell does not know shows as it came, never dropped. */
const tagWords: Readonly<Record<string, string>> = {
  'consumer-not-recorded': 'consumer not recorded',
  'parent-not-on-page': 'parent not on this page',
  cycle: 'cycle',
  'clock-skew': 'clock skew',
}

/**
 * The id the fragment names. It is never trimmed: ids match exactly, so `c-42 ` is another id than `c-42`. A fragment
 * that is not valid percent-encoding is taken as it is.
 */
function idOf(hash: string) {
  const raw = hash.replace(/^#/, '')

  try {
    return decodeURIComponent(raw)
  } catch {
    return raw
  }
}

function plural(count: number, noun: string) {
  return `${count} ${noun}${count === 1 ? '' : 's'}`
}

function GoToHint() {
  return (
    <>
      Check the id, or paste another into Go to id (<Kbd>/</Kbd>).
    </>
  )
}

function SectionLine({ section, onRetry }: { section: TraceSection; onRetry: () => void }) {
  const format = useFormat()

  if (section.stale) {
    return (
      <li className="flex flex-wrap items-center gap-2">
        <strong>{section.displayName}</strong>
        <span>Stale: it did not answer in time.</span>
        <Button variant="ghost" onPress={onRetry}>
          Retry
        </Button>
      </li>
    )
  }

  const count = format.count({ value: section.count, capped: section.limited })

  return (
    <li className="flex flex-wrap items-center gap-2">
      <strong>{section.displayName}</strong>
      {section.limited && section.list ? (
        // The filter rides in history state, so the id never reaches the list's URL.
        <Link
          to={modulePath(section.id, section.list.path)}
          state={section.list.state}
          aria-label={`${count} in ${section.displayName}, open the list`}
        >
          {count}
        </Link>
      ) : (
        <span>{count}</span>
      )}
      {section.unavailable?.length ? (
        <span className="hd-hint">{`${section.unavailable.join(', ')} did not answer`}</span>
      ) : null}
    </li>
  )
}

function TraceBody({ id, trace, onRetry }: { id: string; trace: Trace; onRetry: () => void }) {
  const openObject = useOpenObject()
  const names = new Map(trace.modules.map((section) => [section.id, section.displayName]))
  const events = trace.rows.map((row, index): TimelineEvent => ({
    id: String(index),
    label: row.status ?? row.title,
    tone: row.status ? (row.statusRole ?? 'off') : undefined,
    at: row.at,
    scheduled: row.timestampKind === 'due',
    depth: row.depth,
    tags: row.tags.map((tag) => tagWords[tag] ?? tag),
    text: (
      <>
        {row.status ? `${row.title} · ` : ''}
        {`${names.get(row.module) ?? row.module} ${row.target.kind} `}
        <Mono>{row.target.id}</Mono>
        {row.node ? ` on ${row.node}` : ''}
      </>
    ),
  }))

  function open(index: string) {
    const row = trace.rows[Number(index)]

    if (row) {
      void openObject(row.target).catch(() => undefined)
    }
  }

  return (
    <>
      <ul
        aria-label="Modules on this trace"
        className="flex flex-none flex-col gap-1 border-b border-border px-4 py-2.5"
      >
        {trace.modules.map((section) => (
          <SectionLine key={section.id} section={section} onRetry={onRetry} />
        ))}
        {trace.hidden > 0 ? (
          <li className="hd-hint">{`${plural(trace.hidden, 'module')} hidden by your permissions`}</li>
        ) : null}
      </ul>
      {events.length === 0 ? (
        <EmptyState placement="page" title="No object carries this correlation id">
          <Mono>{id}</Mono> <GoToHint />
        </EmptyState>
      ) : (
        <Timeline label={`Trace of ${id}`} events={events} onSelect={open} />
      )}
    </>
  )
}

/**
 * `{base}/trace#<id>`: every object that carries one correlation id, from every module the user may view, as one tree.
 * The id is in the fragment, so the browser never sends it to the server in a URL.
 */
export default function TracePage() {
  const id = idOf(useLocation().hash)
  const trace = useTrace(id)
  const refresh = () => void trace.refetch()
  const isRefused = trace.error instanceof DashboardApiError && trace.error.status === 400

  usePageTitle('Trace')

  return (
    <div className="hd-frame flex h-full min-h-0 flex-col">
      <PageHeader
        title="Trace"
        actions={
          id ? (
            <>
              {trace.dataUpdatedAt ? (
                <span className="hd-hint">
                  as of <Timestamp value={new Date(trace.dataUpdatedAt).toISOString()} />
                </span>
              ) : null}
              <Button onPress={refresh} isDisabled={isRefused}>
                Refresh
              </Button>
            </>
          ) : undefined
        }
      >
        {id ? (
          <span className="flex min-w-0 items-center gap-1">
            <Mono>{id}</Mono>
            <CopyButton text={id} aria-label="Copy the correlation id" />
          </span>
        ) : null}
      </PageHeader>
      {!id ? (
        <EmptyState placement="page" title="No correlation id">
          Open a trace from a run or a message, or paste a correlation id into Go to id (<Kbd>/</Kbd>).
        </EmptyState>
      ) : isRefused ? (
        // Asking again cannot succeed, so there is no Retry.
        <EmptyState placement="page" title="This id cannot be traced">
          {id.length > maxIdLength
            ? `A correlation id is at most ${maxIdLength} characters; this one has ${id.length}.`
            : 'The dashboard refused this correlation id.'}{' '}
          <GoToHint />
        </EmptyState>
      ) : (
        <QueryState query={trace} label="Loading the trace" rows={6}>
          {(data) => <TraceBody id={id} trace={data} onRetry={refresh} />}
        </QueryState>
      )}
    </div>
  )
}
