import { useDashboard } from '@headless/dashboard-sdk'
import { Button, DetailPanel, FieldGroup, SegmentedControl, SelectField, TextInputField } from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useQuery } from '@headless/dashboard-sdk/query'
import { useState } from 'react'
import { fetchFunctions, fetchPreview } from '../cron.ts'
import { useStale } from '../parts.tsx'

/** The run conditions a chain step offers, in the framework's words, and the `RunCondition` each sends. */
const runConditions = [
  { value: 'OnSuccess', label: 'On success' },
  { value: 'OnFailure', label: 'On failure' },
  { value: 'OnAnyCompletedStatus', label: 'Always' },
] as const

const kinds = [
  { value: 'single', label: 'Single job' },
  { value: 'chain', label: 'Chain' },
] as const

const staleReason = 'Live updates stopped. Refresh before adding.'

interface Step {
  key: number
  function: string
  request: string
  runCondition: string
  children: Step[]
}

let nextKey = 1

function newStep(): Step {
  return { key: nextKey++, function: '', request: '', runCondition: 'OnSuccess', children: [] }
}

/** The functions the form offers, as the registry lists them. */
function useFunctionOptions() {
  const { api } = useDashboard()
  const functions = useQuery({ queryKey: ['jobs', 'functions'], queryFn: () => fetchFunctions(api) })

  return (functions.data ?? []).map((item) => ({ value: item.name, label: item.name }))
}

/** Reads the arguments field: empty sends none, anything else must be JSON, which the server checks against the type. */
function parseRequest(text: string): { value?: unknown; error?: string } {
  if (!text.trim()) {
    return {}
  }

  try {
    return { value: JSON.parse(text) as unknown }
  } catch {
    return { error: 'Arguments must be JSON, such as {"invoiceId": 42}.' }
  }
}

function submitError(caught: unknown) {
  return caught instanceof DashboardApiError ? caught.message : 'Could not create it. Try again.'
}

const chooseFunction = 'Choose a function.'

/** Every step after the first, each with the step it runs after, parents before their children. */
function stepsAfter(step: Step): { step: Step; parent: Step }[] {
  return step.children.flatMap((child) => [{ step: child, parent: step }, ...stepsAfter(child)])
}

export interface CreateFormProps {
  /** The new object opens once the server created it. */
  onCreated: (id: string) => void
  onCancel: () => void
}

/**
 * Adds a time job, or a chain when `chain` is set: one form in the docked panel, its kind switched by `Single job |
 * Chain` without losing what was typed. A chain's later steps sit beside the first, each naming the step it runs
 * after and its run condition. A refused submit shows the server's message and keeps the form.
 */
export function TimeJobForm({ chain, onChainChange, onCreated, onCancel }: CreateFormProps & { chain: boolean; onChainChange: (chain: boolean) => void }) {
  const { api } = useDashboard()
  const stale = useStale()
  const functions = useFunctionOptions()
  const [root, setRoot] = useState<Step>(newStep)
  const [executionTime, setExecutionTime] = useState('')
  const [timeZoneId, setTimeZoneId] = useState('')
  const [description, setDescription] = useState('')
  const [retries, setRetries] = useState('0')
  const [errors, setErrors] = useState<Readonly<Record<number, string>>>({})
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const title = chain ? 'Add chain' : 'Add time job'

  function update(key: number, change: (step: Step) => Step) {
    const walk = (step: Step): Step => (step.key === key ? change(step) : { ...step, children: step.children.map(walk) })
    setRoot((current) => walk(current))
  }

  function remove(key: number) {
    const walk = (step: Step): Step => ({ ...step, children: step.children.filter((child) => child.key !== key).map(walk) })
    setRoot((current) => walk(current))
  }

  async function submit() {
    if (stale || busy) {
      return
    }

    const found: Record<number, string> = {}

    const body = (step: Step, isRoot: boolean): Record<string, unknown> => {
      const request = parseRequest(step.request)

      if (!step.function) {
        found[step.key] = chooseFunction
      } else if (request.error) {
        found[step.key] = request.error
      }

      // A single job keeps the steps typed for a chain, so switching back finds them, but never sends them.
      const children = chain ? step.children : []

      return {
        function: step.function,
        ...(request.value !== undefined ? { request: request.value } : {}),
        ...(isRoot ? {} : { runCondition: step.runCondition }),
        ...(children.length > 0 ? { children: children.map((child) => body(child, false)) } : {}),
      }
    }

    const tree = body(root, true)
    setErrors(found)
    setError(undefined)

    if (Object.keys(found).length > 0) {
      return
    }

    setBusy(true)

    try {
      const created = await api.request<{ id: string }>({
        method: 'POST',
        path: 'jobs/time-jobs',
        body: {
          ...tree,
          ...(executionTime ? { executionTime } : {}),
          ...(timeZoneId ? { timeZoneId } : {}),
          ...(description ? { description } : {}),
          retries: Number(retries) || 0,
        },
      })
      onCreated(created.id)
    } catch (caught) {
      setError(submitError(caught))
    } finally {
      setBusy(false)
    }
  }

  const stepFields = (step: Step) => (
    <>
      <SelectField
        label="Function"
        isRequired
        value={step.function}
        onChange={(value) => update(step.key, (current) => ({ ...current, function: value }))}
        options={functions}
        error={errors[step.key] === chooseFunction ? errors[step.key] : undefined}
      />
      <TextInputField
        label="Arguments (JSON)"
        multiline
        description="The server checks them against the function's argument type."
        value={step.request}
        onChange={(value) => update(step.key, (current) => ({ ...current, request: value }))}
        error={errors[step.key] !== chooseFunction ? errors[step.key] : undefined}
      />
    </>
  )

  return (
    <DetailPanel
      label={title}
      name={title}
      actions={
        <>
          <SegmentedControl label="Kind" size="form" options={kinds} value={chain ? 'chain' : 'single'} onChange={(value) => onChainChange(value === 'chain')} />
          <Button variant="primary" isPending={busy} disabledReason={stale ? staleReason : null} onPress={() => void submit()}>
            {chain ? 'Create chain' : 'Create job'}
          </Button>
          <Button variant="ghost" onPress={onCancel}>
            Cancel
          </Button>
        </>
      }
    >
      <form
        aria-label={chain ? 'First step' : 'Time job'}
        onSubmit={(event) => {
          event.preventDefault()
          void submit()
        }}
      >
        <FieldGroup>{stepFields(root)}</FieldGroup>
        <TextInputField label="Description" value={description} onChange={setDescription} />
        <TextInputField
          label={`Runs at, ${timeZoneId || 'scheduler zone'}`}
          description="A wall-clock time such as 2026-11-06T14:30. Blank runs it now."
          value={executionTime}
          onChange={setExecutionTime}
        />
        <TextInputField label="Time zone" description="An IANA zone. Blank uses the scheduler's zone." value={timeZoneId} onChange={setTimeZoneId} />
        <TextInputField label="Retries" inputMode="numeric" value={retries} onChange={setRetries} />
        {error ? (
          <p role="alert" className="hd-error">
            {error}
          </p>
        ) : null}
      </form>
      {chain ? (
        <section aria-label="Steps after the first">
          <h3 className="hd-label">Runs after it</h3>
          {stepsAfter(root).map(({ step, parent }) => (
            <FieldGroup key={step.key}>
              <p className="hd-faint">{`After ${parent === root ? 'the first step' : parent.function || 'the step above'}`}</p>
              {stepFields(step)}
              <SelectField
                label="Runs"
                isRequired
                value={step.runCondition}
                onChange={(value) => update(step.key, (current) => ({ ...current, runCondition: value }))}
                options={runConditions}
              />
              <span className="hd-actions">
                <Button onPress={() => update(step.key, (current) => ({ ...current, children: [...current.children, newStep()] }))}>Add step after this</Button>
                <Button variant="ghost" onPress={() => remove(step.key)}>
                  Remove step
                </Button>
              </span>
            </FieldGroup>
          ))}
          <span>
            <Button onPress={() => update(root.key, (current) => ({ ...current, children: [...current.children, newStep()] }))}>Add step</Button>
          </span>
        </section>
      ) : null}
    </DetailPanel>
  )
}

/** Adds a recurring job: the expression shows the server's plain reading and next five fires before it is submitted. */
export function CronJobForm({ onCreated, onCancel }: CreateFormProps) {
  const { api } = useDashboard()
  const format = useFormat()
  const stale = useStale()
  const functions = useFunctionOptions()
  const [fn, setFunction] = useState('')
  const [expression, setExpression] = useState('')
  const [timeZoneId, setTimeZoneId] = useState('')
  const [request, setRequest] = useState('')
  const [description, setDescription] = useState('')
  const [fieldErrors, setFieldErrors] = useState<{ function?: string; request?: string }>({})
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const preview = useQuery({
    queryKey: ['jobs', 'cron-preview', expression, timeZoneId],
    queryFn: () => fetchPreview(api, expression, timeZoneId),
    enabled: expression.trim() !== '',
  })

  async function submit() {
    if (stale || busy) {
      return
    }

    const parsed = parseRequest(request)
    const found = { function: fn ? undefined : chooseFunction, request: parsed.error }
    setFieldErrors(found)
    setError(undefined)

    if (found.function || found.request) {
      return
    }

    setBusy(true)

    try {
      const created = await api.request<{ id: string }>({
        method: 'POST',
        path: 'jobs/cron-jobs',
        body: {
          function: fn,
          expression,
          ...(timeZoneId ? { timeZoneId } : {}),
          ...(parsed.value !== undefined ? { request: parsed.value } : {}),
          ...(description ? { description } : {}),
        },
      })
      onCreated(created.id)
    } catch (caught) {
      setError(submitError(caught))
    } finally {
      setBusy(false)
    }
  }

  const answer = preview.data

  return (
    <DetailPanel
      label="Add recurring job"
      name="Add recurring job"
      actions={
        <>
          <Button variant="primary" isPending={busy} disabledReason={stale ? staleReason : null} onPress={() => void submit()}>
            Create recurring job
          </Button>
          <Button variant="ghost" onPress={onCancel}>
            Cancel
          </Button>
        </>
      }
    >
      <form
        aria-label="Recurring job"
        onSubmit={(event) => {
          event.preventDefault()
          void submit()
        }}
      >
        <SelectField label="Function" isRequired value={fn} onChange={setFunction} options={functions} error={fieldErrors.function} />
        <TextInputField label="Description" value={description} onChange={setDescription} />
        <TextInputField label="Expression" isRequired value={expression} onChange={setExpression} error={answer?.validationMessage} />
        <TextInputField label="Time zone" description="An IANA zone. Blank uses the scheduler's zone." value={timeZoneId} onChange={setTimeZoneId} />
        <TextInputField
          label="Arguments (JSON)"
          multiline
          description="The server checks them against the function's argument type."
          value={request}
          onChange={setRequest}
          error={fieldErrors.request}
        />
        {error ? (
          <p role="alert" className="hd-error">
            {error}
          </p>
        ) : null}
      </form>
      <section aria-label="Schedule preview">
        <h3 className="hd-label">{answer && !answer.validationMessage ? `Next fires, ${answer.timeZoneId}` : 'Schedule preview'}</h3>
        {answer && !answer.validationMessage ? (
          <>
            <p>{answer.reading}</p>
            <ol aria-label={`Next fires, ${answer.timeZoneId}`}>
              {answer.nextFires.map((fire) => (
                <li key={fire}>{format.zonedTime(fire, answer.timeZoneId)}</li>
              ))}
            </ol>
          </>
        ) : (
          <p className="hd-hint">Type an expression to see how the server reads it and when it fires next.</p>
        )}
      </section>
    </DetailPanel>
  )
}
