import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { ActionConfirm, createCommandsApi, type ConfirmedAction } from '@headless/dashboard-sdk/commands'
import {
  Announcer,
  Banner,
  Button,
  Callout,
  EmptyState,
  FieldGrid,
  GridField,
  LoadError,
  LookupForm,
  Meter,
  Mono,
  PageFrame,
  PageHeader,
  QueryState,
  SelectField,
  StatCard,
  StatStrip,
  StatusMark,
  Text,
  TextInputField,
  Timestamp,
  usePageFrame,
} from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { DeleteIcon, SearchIcon } from '@headless/dashboard-sdk/icons'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { DataTable } from '@headless/dashboard-sdk/table'
import { useEffect, useRef, useState, type FormEvent, type RefObject } from 'react'
import {
  formatCountdown,
  formatWindow,
  getCatalogue,
  lookUp,
  resetArguments,
  resetCommandId,
  subjectId,
  toWindowSeconds,
  windowUnits,
  type Catalogue,
  type LookupCall,
  type LookupView,
  type WindowUnit,
} from '../api.ts'

const customPurpose = '__custom'
const noAttempts = 'No attempts in the current window'

/**
 * A lookup and what it answered. The subject lives only here, in component state: never in a URL, a route state, or
 * the query cache, so it leaves with the page.
 */
type Lookup =
  | { state: 'idle' }
  | { state: 'pending'; call: LookupCall }
  | { state: 'found'; call: LookupCall; view: LookupView; readAt: number }
  | { state: 'failed'; call: LookupCall; message: string }

const storeLabels = { named: 'Named cache', remote: 'Remote cache', default: 'Default cache' } as const

export default function RateLimitsView() {
  const { api } = useDashboard()
  const module = useModule()
  const format = useFormat()
  const catalogue = useQuery({
    queryKey: ['rate-limiting', 'catalogue'],
    queryFn: () => getCatalogue(api),
    staleTime: Infinity,
  })
  const purposes = catalogue.data?.purposes ?? []
  const frame = usePageFrame({ viewId: 'rate-limiting.rate-limits', rows: purposes.map((p) => ({ ...p, id: p.name })) })

  const [purpose, setPurpose] = useState('')
  const [custom, setCustom] = useState({ name: '', limit: '', window: '', unit: 'minutes' as WindowUnit })
  const [subject, setSubject] = useState('')
  const [formError, setFormError] = useState<string>()
  const [lookup, setLookup] = useState<Lookup>({ state: 'idle' })
  const [announcement, setAnnouncement] = useState({ id: 0, message: '' })
  const [isConfirming, setIsConfirming] = useState(false)
  const [isResetting, setIsResetting] = useState(false)
  const [resetError, setResetError] = useState<string>()
  const [isStale, setIsStale] = useState(false)
  const [focusAfterReset, setFocusAfterReset] = useState(false)
  const resultRef = useRef<HTMLElement>(null)

  // The first declared purpose is picked once the catalogue arrives, so the form opens ready to look up.
  const selected = purpose || purposes[0]?.name || customPurpose
  const isCustom = selected === customPurpose

  const windowEndsAt = lookup.state === 'found' ? lookup.view.windowEndsAt : undefined

  // The countdown is the one the server read at lookup time; once the window has closed it no longer says anything
  // true, so a note replaces it rather than a countdown past zero.
  useEffect(() => {
    setIsStale(false)

    if (!windowEndsAt) {
      return undefined
    }

    const timer = setTimeout(() => setIsStale(true), Math.max(0, Date.parse(windowEndsAt) - Date.now()))

    return () => clearTimeout(timer)
  }, [windowEndsAt])

  useEffect(() => {
    if (focusAfterReset && lookup.state === 'found') {
      resultRef.current?.focus()
      setFocusAfterReset(false)
    }
  }, [focusAfterReset, lookup.state])

  // Any edit to what was looked up clears the answer, so a Reset never acts on a subject other than the one shown.
  function edit(apply: () => void) {
    apply()
    setFormError(undefined)
    setLookup({ state: 'idle' })
  }

  function announce(message: string) {
    setAnnouncement((current) => ({ id: current.id + 1, message }))
  }

  async function run(call: LookupCall) {
    setLookup({ state: 'pending', call })

    try {
      const view = await lookUp(api, call)
      setLookup({ state: 'found', call, view, readAt: Date.now() })
      announce(`${view.isLockedOut ? 'Locked out' : 'Within limit'}: ${view.count} of ${view.limit} attempts used.`)
    } catch (error) {
      const message = error instanceof Error ? error.message : 'The lookup failed. Try again.'
      setLookup({ state: 'failed', call, message })
      announce(message)
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!subject) {
      setFormError('Type the subject to look up.')
      return
    }

    if (!isCustom) {
      void run({ purpose: selected, subject })
      return
    }

    const limit = Number(custom.limit)
    const windowSeconds = toWindowSeconds(custom.window, custom.unit)

    if (!custom.name.trim() || !Number.isInteger(limit) || limit < 1 || windowSeconds === undefined) {
      setFormError('A custom purpose needs its name, a limit of at least 1, and a whole window.')
      return
    }

    void run({ purpose: custom.name.trim(), subject, quota: { limit, windowSeconds } })
  }

  async function reset(action: ConfirmedAction) {
    if (lookup.state !== 'found') {
      return
    }

    const { call, view } = lookup
    setIsResetting(true)
    setResetError(undefined)

    try {
      const [row] = await createCommandsApi(api, module.id).run(resetCommandId, {
        ids: [subjectId(view)],
        arguments: resetArguments(call),
        reason: action.reason,
        confirmed: true,
      })

      if (row?.outcome === 'failed') {
        setResetError(row.reason ?? 'The reset failed. Try again.')
        return
      }

      setIsConfirming(false)
      setFocusAfterReset(true)
      await run(call)
    } catch (error) {
      setResetError(error instanceof DashboardApiError ? error.message : 'The reset failed. Try again.')
    } finally {
      setIsResetting(false)
    }
  }

  return (
    <PageFrame
      frame={frame}
      stats={catalogue.data ? <RateLimitStats catalogue={catalogue.data} /> : undefined}
      header={<PageHeader title="Rate limits" description="Look up one subject's attempts. Counts can't be browsed." />}
    >
      <Announcer id={announcement.id} message={announcement.message} />
      <QueryState query={catalogue} label="Loading the declared purposes" rows={3} shape="table">
        {(data) => (
          <>
            {data.purposes.length === 0 ? (
              <EmptyState icon={SearchIcon} title="No purposes declared">
                A lookup needs a declared purpose, or a custom purpose with the quota the app acquires it with. Declare
                each flow in AttemptLimiterOptions.Purposes so the screen lists it with its quota.
              </EmptyState>
            ) : null}
            <LookupForm
              title="Look up a subject"
              icon={SearchIcon}
              isPending={lookup.state === 'pending'}
              onSubmit={submit}
              hint="Type the subject exactly as the app passes it to the limiter: the server does not normalise it."
              result={
                <LookupResult
                  lookup={lookup}
                  isStale={isStale}
                  resultRef={resultRef}
                  canOperate={module.canOperate}
                  onReset={() => {
                    setResetError(undefined)
                    setIsConfirming(true)
                  }}
                  onRetry={() => lookup.state === 'failed' && void run(lookup.call)}
                  onRefresh={() => lookup.state === 'found' && void run(lookup.call)}
                  asOf={lookup.state === 'found' ? format.exactTime(lookup.readAt) : ''}
                />
              }
            >
              <SelectField
                label="Purpose"
                width="medium"
                isRequired
                value={selected}
                onChange={(value) => edit(() => setPurpose(value))}
                options={[
                  ...data.purposes.map((p) => ({
                    value: p.name,
                    label: `${p.name} · ${p.limit} / ${formatWindow(p.windowSeconds)}`,
                  })),
                  { value: customPurpose, label: 'Custom purpose' },
                ]}
              />
              {isCustom ? (
                <>
                  <TextInputField
                    label="Custom purpose"
                    width="medium"
                    isRequired
                    value={custom.name}
                    onChange={(name) => edit(() => setCustom((current) => ({ ...current, name })))}
                    maxLength={200}
                  />
                  <TextInputField
                    label="Limit"
                    width="short"
                    isRequired
                    inputMode="numeric"
                    value={custom.limit}
                    onChange={(limit) => edit(() => setCustom((current) => ({ ...current, limit })))}
                  />
                  <TextInputField
                    label="Window"
                    width="short"
                    isRequired
                    inputMode="numeric"
                    value={custom.window}
                    onChange={(window) => edit(() => setCustom((current) => ({ ...current, window })))}
                  />
                  <SelectField
                    label="Window unit"
                    width="short"
                    isRequired
                    value={custom.unit}
                    onChange={(unit) =>
                      edit(() =>
                        setCustom((current) => ({
                          ...current,
                          unit: windowUnits.find((candidate) => candidate === unit) ?? 'minutes',
                        })),
                      )
                    }
                    options={windowUnits.map((unit) => ({ value: unit, label: unit }))}
                  />
                </>
              ) : null}
              <TextInputField
                label="Subject"
                width="medium"
                isRequired
                maxLength={512}
                value={subject}
                error={formError}
                onChange={(value) => edit(() => setSubject(value))}
              />
            </LookupForm>
            <PurposesTable catalogue={data} />
            <Callout title="How it works">
              <p>
                The server turns the subject into a fingerprint with HMAC-SHA256 and the app's SubjectKey, the same way
                the app does. The subject and the key never leave the server.
              </p>
              <p>
                Each purpose counts attempts in fixed windows. A look-up reads the current window without spending an
                attempt.
              </p>
              <p>Reset deletes the current window's counter only. The next window starts from zero as usual.</p>
              <p>Rotating SubjectKey or changing a window starts every counter over.</p>
              <p>
                A look-up and a reset use the declared quota, so an app that acquires a purpose with another window
                reads zero here. Through a hybrid cache a look-up can read a stale local copy; point the limiter at the
                remote cache before you rely on these counts.
              </p>
            </Callout>
          </>
        )}
      </QueryState>
      {lookup.state === 'found' ? (
        <ActionConfirm
          isOpen={isConfirming}
          onCancel={() => setIsConfirming(false)}
          onConfirm={(action) => void reset(action)}
          title={`Reset this window of ${lookup.view.purpose}`}
          reach={
            <>
              This lifts brute-force protection for this subject until the window ends at{' '}
              {format.exactTime(lookup.view.windowEndsAt)}. Recorded in the operator log by fingerprint; don't repeat
              the subject in the reason.
            </>
          }
          confirmLabel="Reset this window"
          cancelLabel="Keep the counter"
          confirmation="confirm"
          reason="required"
          isBusy={isResetting}
          error={resetError}
          tone="danger"
        />
      ) : null}
    </PageFrame>
  )
}

function RateLimitStats({ catalogue }: { catalogue: Catalogue }) {
  const { purposes, shortest, counterStore, cacheName } = catalogue

  return (
    <StatStrip label="Rate limits">
      <StatCard label="Purposes" value={purposes.length} caption="declared by the app" />
      {shortest ? (
        <StatCard label="Shortest window" value={formatWindow(shortest.windowSeconds)} caption={shortest.name} />
      ) : null}
      {counterStore ? (
        <StatCard
          label="Counter store"
          value={storeLabels[counterStore]}
          caption={counterStore === 'named' ? cacheName : 'the remote or default cache'}
        />
      ) : null}
    </StatStrip>
  )
}

function PurposesTable({ catalogue }: { catalogue: Catalogue }) {
  return (
    <DataTable
      label="Purposes the app declares"
      rows={catalogue.purposes.map((purpose) => ({ ...purpose, id: purpose.name }))}
      emptyTitle="No purposes declared"
      emptyState="Declare each flow in AttemptLimiterOptions.Purposes."
      columns={[
        { id: 'purpose', header: 'Purpose', cell: (row) => <Mono>{row.name}</Mono>, isRowHeader: true, kind: 'id' },
        { id: 'limit', header: 'Limit', cell: (row) => row.limit, kind: 'number', align: 'end' },
        { id: 'window', header: 'Window', cell: (row) => formatWindow(row.windowSeconds), align: 'end' },
      ]}
    />
  )
}

interface LookupResultProps {
  lookup: Lookup
  isStale: boolean
  resultRef: RefObject<HTMLElement | null>
  canOperate: boolean
  asOf: string
  onReset: () => void
  onRetry: () => void
  onRefresh: () => void
}

function LookupResult({
  lookup,
  isStale,
  resultRef,
  canOperate,
  asOf,
  onReset,
  onRetry,
  onRefresh,
}: LookupResultProps) {
  switch (lookup.state) {
    case 'idle':
    case 'pending':
      return null
    case 'failed':
      // A failure never shows a count: a zero here would read as a subject with no attempts.
      return <LoadError message={lookup.message} onRetry={onRetry} />
    case 'found': {
      const { view } = lookup
      const resetRefusal = !canOperate ? 'Needs operate permission' : view.count === 0 ? noAttempts : undefined

      return (
        <section ref={resultRef} tabIndex={-1} aria-label="Lookup result">
          <Text tone="title" as="h3">
            {view.isLockedOut ? 'Locked out' : 'Within limit'}
          </Text>
          <StatusMark
            tone={view.isLockedOut ? 'fail' : 'ok'}
            label={view.isLockedOut ? 'Locked out' : 'Within limit'}
          />
          <Meter
            label="Attempts in this window"
            value={Math.min(view.count, view.limit)}
            max={view.limit}
            valueLabel={`${view.count} of ${view.limit} used, ${view.remaining} remaining; refused attempts count too`}
            tone={view.isLockedOut ? 'fail' : 'ok'}
          />
          <FieldGrid columns={3} label="Window">
            <GridField label="Purpose">
              <Mono>{view.purpose}</Mono>
            </GridField>
            <GridField label="Fingerprint">
              <Mono>{view.fingerprint}</Mono>
            </GridField>
            <GridField label="Quota">
              {view.quota.limit} / {formatWindow(view.quota.windowSeconds)}
              {view.declared ? '' : ' (custom)'}
            </GridField>
            <GridField label="Window starts">
              <Timestamp value={view.windowStartsAt} />
            </GridField>
            <GridField label="Window ends">
              <Timestamp value={view.windowEndsAt} />
            </GridField>
            <GridField label="Closes in">
              {isStale
                ? 'This window has closed; refresh to read the current one.'
                : `${formatCountdown(view.closesInSeconds)} as of ${asOf}`}
            </GridField>
          </FieldGrid>
          {isStale ? <Banner tone="warn">The window shown has closed. Refresh to read the current one.</Banner> : null}
          <Button onPress={onRefresh}>Refresh</Button>
          <Button variant="danger" icon={DeleteIcon} disabledReason={resetRefusal} onPress={onReset}>
            Reset this window
          </Button>
        </section>
      )
    }
  }
}
