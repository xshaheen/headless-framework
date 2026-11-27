import { useDashboard } from '@headless/dashboard-sdk'
import {
  Actions,
  Announcer,
  Banner,
  Button,
  Callout,
  CopyButton,
  EmptyState,
  FieldGrid,
  GridField,
  LookupForm,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  StatCard,
  StatStrip,
  StatusMark,
  Text,
  TextInputField,
  Timestamp,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { AttentionIcon, SearchIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { useEffect, useRef, useState, type FormEvent, type RefObject } from 'react'
import {
  formatDuration,
  getStore,
  lookupCall,
  lookUp,
  recordState,
  type LookupCall,
  type RecordView,
  type StoreView,
} from '../api.ts'

const noRows: { id: string }[] = []
const unavailable = 'The idempotency store is unavailable. Try again.'

/**
 * A lookup and what it answered. The key lives only here, in component state: never in a URL, a route state, or the
 * query cache, so it leaves with the page.
 */
type Lookup =
  | { state: 'idle' }
  | { state: 'pending'; call: LookupCall }
  | { state: 'found'; call: LookupCall; record: RecordView }
  | { state: 'not-found'; call: LookupCall }
  | { state: 'invalid'; call: LookupCall; message: string }
  | { state: 'failed'; call: LookupCall; message: string }

export default function IdempotencyView() {
  const { api } = useDashboard()
  const store = useQuery({ queryKey: ['idempotency', 'store'], queryFn: () => getStore(api), staleTime: Infinity })
  const frame = usePageFrame({ viewId: 'idempotency.idempotency', rows: noRows })

  const [tenant, setTenant] = useState('')
  const [key, setKey] = useState('')
  const [formError, setFormError] = useState<string>()
  const [lookup, setLookup] = useState<Lookup>({ state: 'idle' })
  const [announcement, setAnnouncement] = useState({ id: 0, message: '' })
  const [focusResult, setFocusResult] = useState(false)
  const resultRef = useRef<HTMLElement>(null)
  // Each lookup's generation: an answer commits only while its lookup is still the latest, so a slow answer for an
  // edited or replaced key never lands as the result.
  const latest = useRef(0)

  // Look up again and Retry unmount the control that started them while the lookup is pending, so focus returns to the
  // answer once it settles instead of falling to the page.
  useEffect(() => {
    if (focusResult && lookup.state !== 'idle' && lookup.state !== 'pending') {
      resultRef.current?.focus()
      setFocusResult(false)
    }
  }, [focusResult, lookup.state])

  // Any edit to what was looked up clears the answer and drops a pending one, so the result always names the key and
  // tenant the form shows.
  function edit(apply: () => void) {
    apply()
    latest.current += 1
    setFormError(undefined)
    setFocusResult(false)
    setLookup({ state: 'idle' })
  }

  function announce(message: string) {
    setAnnouncement((current) => ({ id: current.id + 1, message }))
  }

  async function run(call: LookupCall) {
    const id = ++latest.current
    setLookup({ state: 'pending', call })

    try {
      const view = await lookUp(api, call)

      if (id !== latest.current) {
        return
      }

      if (view.found) {
        setLookup({ state: 'found', call, record: view.record })
        announce(`${recordState(view.record).label}.`)
      } else {
        setLookup({ state: 'not-found', call })
        announce(`No record for this key in ${scopeName(call)}.`)
      }
    } catch (error) {
      if (id !== latest.current) {
        return
      }

      if (error instanceof DashboardApiError && error.status === 400) {
        setLookup({ state: 'invalid', call, message: error.message })
        announce(error.message)
      } else {
        const message =
          error instanceof DashboardApiError && error.status === 503
            ? unavailable
            : error instanceof Error
              ? error.message
              : 'The lookup failed. Try again.'
        setLookup({ state: 'failed', call, message })
        announce(message)
      }
    }
  }

  function lookUpAgain(call: LookupCall) {
    setFocusResult(true)
    void run(call)
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!key) {
      setFormError('Type the idempotency key to look up.')
      return
    }

    void run(lookupCall(key, tenant))
  }

  return (
    <PageFrame
      frame={frame}
      stats={store.data ? <StoreStats store={store.data} /> : undefined}
      header={
        <PageHeader
          title="Idempotency"
          description="Look up one key: was this operation admitted, is it running, or did it complete? Records can't be browsed."
        />
      }
    >
      <Announcer id={announcement.id} message={announcement.message} />
      <QueryState query={store} label="Loading the idempotency store" rows={2} shape="rows">
        {() => (
          <>
            <LookupForm
              title="Look up a key"
              icon={SearchIcon}
              isPending={lookup.state === 'pending'}
              onSubmit={submit}
              hint={
                <>
                  A blank Tenant reads the records admitted outside any tenant; it never searches every tenant. The read
                  takes no lock and changes nothing. HTTP middleware records are keyed by a SHA-256 of the request scope
                  under the caller's tenant claim, so the client's Idempotency-Key alone does not find them.
                </>
              }
              result={
                <LookupResult
                  lookup={lookup}
                  resultRef={resultRef}
                  onAgain={() => lookup.state !== 'idle' && lookup.state !== 'pending' && lookUpAgain(lookup.call)}
                />
              }
            >
              <TextInputField
                label="Tenant"
                width="medium"
                description="Leave blank for the host scope (no tenant)."
                maxLength={128}
                value={tenant}
                onChange={(value) => edit(() => setTenant(value))}
              />
              <TextInputField
                label="Idempotency key"
                isRequired
                maxLength={256}
                value={key}
                error={formError}
                onChange={(value) => edit(() => setKey(value))}
              />
            </LookupForm>
            <Callout title="Why this screen is read-only">
              <p>
                Each tenant-scoped key has one record. An admission replays a completed result, answers InFlight while a
                live lease owns the key, or admits the caller under a new generation; a takeover means an earlier
                attempt stalled past its lease.
              </p>
              <p>
                There is no release, expire, or delete here: the store has no operator API for them, and releasing a
                live attempt could run its operation twice.
              </p>
              <p>
                Stored result and recovery bodies never reach this screen, because they can hold personal data. It shows
                only their contract and size.
              </p>
            </Callout>
          </>
        )}
      </QueryState>
    </PageFrame>
  )
}

function scopeName(call: LookupCall): string {
  return call.tenantId ? `tenant ${call.tenantId}` : 'the host scope (no tenant)'
}

function StoreStats({ store }: { store: StoreView }) {
  return (
    <StatStrip label="Idempotency store">
      <StatCard label="Provider" value={providerName(store.provider)} caption={store.location ?? 'In process'} />
      <StatCard
        label="Default retention"
        value={formatDuration(store.defaultRetentionSeconds)}
        caption="completed records replay"
      />
      <StatCard
        label="Default lease"
        value={formatDuration(store.defaultLeaseSeconds)}
        caption={`${formatDuration(store.minimumLeaseSeconds)} to ${formatDuration(store.maximumLeaseSeconds)}`}
      />
      <StatCard
        label="Purge"
        value={store.purgeIntervalSeconds === undefined ? 'Off' : `every ${formatDuration(store.purgeIntervalSeconds)}`}
        caption={
          store.purgeIntervalSeconds === undefined
            ? 'The purge is off, or the records expire on their own'
            : `up to ${store.purgeBatchSize} records a statement`
        }
      />
    </StatStrip>
  )
}

/** `Headless.Idempotency.PostgreSql` reads `PostgreSql`; the full package name is the caption's hover text. */
function providerName(provider: string): string {
  return provider.startsWith('Headless.Idempotency.') ? provider.slice('Headless.Idempotency.'.length) : provider
}

function joinParts(...parts: (string | undefined)[]): string {
  return parts.filter((part) => part !== undefined).join(' · ')
}

interface LookupResultProps {
  lookup: Lookup
  resultRef: RefObject<HTMLElement | null>
  onAgain: () => void
}

function LookupResult({ lookup, resultRef, onAgain }: LookupResultProps) {
  switch (lookup.state) {
    case 'idle':
    case 'pending':
      return null
    case 'invalid':
      return (
        <section ref={resultRef} tabIndex={-1} aria-label="Lookup result">
          <Banner tone="error">
            <AttentionIcon />
            <span role="alert">{lookup.message}</span>
          </Banner>
        </section>
      )
    case 'failed':
      // A failure never shows a previous answer or a not-found: either would read as the store's answer.
      return (
        <section ref={resultRef} tabIndex={-1} aria-label="Lookup result">
          <Banner tone="error">
            <AttentionIcon />
            <span role="alert">{lookup.message}</span>
            <Button onPress={onAgain}>Retry</Button>
          </Banner>
        </section>
      )
    case 'not-found':
      return (
        <section ref={resultRef} tabIndex={-1} aria-label="Lookup result">
          <EmptyState
            icon={SearchIcon}
            title={`No record for this key in ${scopeName(lookup.call)}`}
            action={<Button onPress={onAgain}>Look up again</Button>}
          >
            The key was never admitted there, or its retention ended and no live attempt holds it. A record admitted
            under another tenant is found only with that tenant.
          </EmptyState>
        </section>
      )
    case 'found':
      return <FoundRecord call={lookup.call} record={lookup.record} resultRef={resultRef} onAgain={onAgain} />
  }
}

interface FoundRecordProps {
  call: LookupCall
  record: RecordView
  resultRef: RefObject<HTMLElement | null>
  onAgain: () => void
}

function FoundRecord({ call, record, resultRef, onAgain }: FoundRecordProps) {
  const format = useFormat()
  const status = recordState(record)

  return (
    <section ref={resultRef} tabIndex={-1} aria-label="Lookup result">
      <Text tone="title" as="h3">
        <StatusMark tone={status.tone} label={status.label} />
      </Text>
      <Text tone="hint">{status.meaning}</Text>
      {status.state === 'past-retention' ? (
        <Banner tone="info" role="status">
          Kept past retention: retention ended at {format.exactTime(record.retentionUntil)}, and the record stays until
          the attempt ends.
        </Banner>
      ) : null}
      <FieldGrid columns={3} label="Record">
        <GridField label="Key" action={<CopyButton text={call.key} aria-label="Copy key" />}>
          <Mono>{call.key}</Mono>
        </GridField>
        <GridField label="Tenant">{call.tenantId ? <Mono>{call.tenantId}</Mono> : 'Host scope'}</GridField>
        <GridField label="Generation">{record.generation ?? 'None'}</GridField>
        {record.takeoverCount === undefined ? null : <GridField label="Takeovers">{record.takeoverCount}</GridField>}
        <GridField label="Fingerprint" span={2}>
          <Mono>
            {record.fingerprint.algorithm} · {record.fingerprint.hash}
          </Mono>
        </GridField>
        {record.leaseExpiresAt ? (
          <GridField label={record.isLeaseExpired ? 'Lease expired at' : 'Lease expires at'}>
            <Timestamp value={record.leaseExpiresAt} />
          </GridField>
        ) : null}
        <GridField label="Retention until">
          <Timestamp value={record.retentionUntil} />
        </GridField>
        {record.resultContract !== undefined || record.resultSizeBytes !== undefined ? (
          <GridField label="Stored result" span={2}>
            {joinParts(
              record.resultContract,
              record.resultSizeBytes === undefined ? undefined : format.size(record.resultSizeBytes),
            )}
          </GridField>
        ) : null}
        {record.recoveryPoint !== undefined ? (
          <GridField label="Recovery point" span={2}>
            {joinParts(
              record.recoveryPoint,
              record.recoveryContract,
              record.recoveryStateSizeBytes === undefined ? undefined : format.size(record.recoveryStateSizeBytes),
            )}
          </GridField>
        ) : null}
      </FieldGrid>
      <Actions>
        <Text tone="hint">
          As of <Timestamp value={record.observedAt} /> by the store's clock
        </Text>
        <Button onPress={onAgain}>Look up again</Button>
      </Actions>
    </section>
  )
}
