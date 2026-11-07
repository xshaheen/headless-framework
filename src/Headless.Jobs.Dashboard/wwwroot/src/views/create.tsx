import { useDashboard } from '@headless/dashboard-sdk'
import { Button, FieldGroup, Inspector, SelectField, TextInputField } from '@headless/dashboard-sdk/components'
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

interface StepFieldsProps {
step: Step
depth: number
chain: boolean
functions: readonly { value: string; label: string }[]
errors: Readonly<Record<number, string>>
update: (key: number, change: (step: Step) => Step) => void
remove: (key: number) => void
}

/** One step and the steps that run after it, indented one level per parent. */
function StepFields({ step, depth, chain, functions, errors, update, remove }: StepFieldsProps) {
  const isRoot = depth === 0

  return (
    <li style={{ paddingInlineStart: `${depth * 1.5}rem`, listStyle: 'none' }} aria-label={isRoot ? 'First step' : `Step after its parent, level ${depth}`}>
      <FieldGroup>
        <SelectField
          label="Function"
          isRequired
          value={step.function}
          onChange={(value) => update(step.key, (current) => ({ ...current, function: value }))}
          options={functions}
          error={errors[step.key] === 'Choose a function.' ? errors[step.key] : undefined}
        />
        {isRoot ? null : (
          <SelectField
            label="Runs"
            isRequired
            value={step.runCondition}
            onChange={(value) => update(step.key, (current) => ({ ...current, runCondition: value }))}
            options={runConditions}
          />
        )}
        <TextInputField
          label="Arguments (JSON)"
          multiline
          description="The server checks them against the function's argument type."
          value={step.request}
          onChange={(value) => update(step.key, (current) => ({ ...current, request: value }))}
          error={errors[step.key] !== 'Choose a function.' ? errors[step.key] : undefined}
        />
        {chain ? (
          <div className="hd-actions">
            <Button onPress={() => update(step.key, (current) => ({ ...current, children: [...current.children, newStep()] }))}>
              Add step after this
            </Button>
            {isRoot ? null : (
              <Button variant="ghost" onPress={() => remove(step.key)}>
                Remove step
              </Button>
            )}
          </div>
        ) : null}
      </FieldGroup>
      {step.children.length > 0 ? (
        <ol>
          {step.children.map((child) => (
            <StepFields key={child.key} {...{ chain, functions, errors, update, remove }} step={child} depth={depth + 1} />
          ))}
        </ol>
      ) : null}
    </li>
  )
}

export interface CreateFormProps {
  /** The new object is selected once the server created it. */
  onCreated: (id: string) => void
  onCancel: () => void
}

/**
 * Adds a time job, or a chain when `chain` is set: one form in the inspector. A chain is built as an indented tree of
 * steps, each after its parent with a run condition. A refused submit shows the server's message and keeps the form.
 */
export function TimeJobForm({ chain, onCreated, onCancel }: CreateFormProps & { chain?: boolean }) {
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
  const noun = chain ? 'chain' : 'time job'

  function update(key: number, change: (step: Step) => Step) {
    const walk = (step: Step): Step => (step.key === key ? change(step) : { ...step, children: step.children.map(walk) })
    setRoot((current) => walk(current))
  }

  function remove(key: number) {
    const walk = (step: Step): Step => ({ ...step, children: step.children.filter((child) => child.key !== key).map(walk) })
    setRoot((current) => walk(current))
  }

  async function submit() {
    const found: Record<number, string> = {}

    const body = (step: Step, isRoot: boolean): Record<string, unknown> => {
      const request = parseRequest(step.request)

      if (!step.function) {
        found[step.key] = 'Choose a function.'
      } else if (request.error) {
        found[step.key] = request.error
      }

      return {
        function: step.function,
        ...(request.value !== undefined ? { request: request.value } : {}),
        ...(isRoot ? {} : { runCondition: step.runCondition }),
        ...(step.children.length > 0 ? { children: step.children.map((child) => body(child, false)) } : {}),
      }
    }

    if (stale) {
      return
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

  return (
    <Inspector label={chain ? 'Add chain' : 'Add time job'} onClose={onCancel}>
      <h2 className="hd-title">{chain ? 'Add chain' : 'Add time job'}</h2>
      <form
        className="hd-fields"
        onSubmit={(event) => {
          event.preventDefault()
          void submit()
        }}
      >
        <ol aria-label={chain ? 'Chain steps' : 'Time job'} style={{ paddingInlineStart: 0 }}>
          <StepFields step={root} depth={0} chain={chain === true} functions={functions} errors={errors} update={update} remove={remove} />
        </ol>
        <TextInputField
          label={`Runs at, ${timeZoneId || 'scheduler zone'}`}
          description="A wall-clock time such as 2026-11-06T14:30. Blank runs it now."
          value={executionTime}
          onChange={setExecutionTime}
        />
        <TextInputField label="Time zone" description="An IANA zone. Blank uses the scheduler's zone." value={timeZoneId} onChange={setTimeZoneId} />
        <TextInputField label="Retries" inputMode="numeric" value={retries} onChange={setRetries} />
        <TextInputField label="Description" value={description} onChange={setDescription} />
        {error ? (
          <p role="alert" className="hd-error">
            {error}
          </p>
        ) : null}
        <div className="hd-actions">
          <Button
            type="submit"
            variant="primary"
            isPending={busy}
            disabledReason={stale ? 'Live updates stopped. Refresh before adding.' : null}
          >
            {`Add ${noun}`}
          </Button>
          <Button variant="ghost" onPress={onCancel}>
            Cancel
          </Button>
        </div>
      </form>
    </Inspector>
  )
}

/** Adds a cron job: the expression shows the server's plain reading and next five fires before it is submitted. */
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
    if (stale) {
      return
    }

    const parsed = parseRequest(request)
    const found = { function: fn ? undefined : 'Choose a function.', request: parsed.error }
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
    <Inspector label="Add cron job" onClose={onCancel}>
      <h2 className="hd-title">Add cron job</h2>
      <form
        className="hd-fields"
        onSubmit={(event) => {
          event.preventDefault()
          void submit()
        }}
      >
        <SelectField label="Function" isRequired value={fn} onChange={setFunction} options={functions} error={fieldErrors.function} />
        <TextInputField label="Expression" isRequired value={expression} onChange={setExpression} error={answer?.validationMessage} />
        {answer && !answer.validationMessage ? (
          <section aria-label="Schedule preview" className="hd-hint">
            <p>{answer.reading}</p>
            <ol aria-label={`Next fires, ${answer.timeZoneId}`}>
              {answer.nextFires.map((fire) => (
                <li key={fire}>{format.zonedTime(fire, answer.timeZoneId)}</li>
              ))}
            </ol>
          </section>
        ) : null}
        <TextInputField label="Time zone" description="An IANA zone. Blank uses the scheduler's zone." value={timeZoneId} onChange={setTimeZoneId} />
        <TextInputField
          label="Arguments (JSON)"
          multiline
          description="The server checks them against the function's argument type."
          value={request}
          onChange={setRequest}
          error={fieldErrors.request}
        />
        <TextInputField label="Description" value={description} onChange={setDescription} />
        {error ? (
          <p role="alert" className="hd-error">
            {error}
          </p>
        ) : null}
        <div className="hd-actions">
          <Button
            type="submit"
            variant="primary"
            isPending={busy}
            disabledReason={stale ? 'Live updates stopped. Refresh before adding.' : null}
          >
            Add cron job
          </Button>
          <Button variant="ghost" onPress={onCancel}>
            Cancel
          </Button>
        </div>
      </form>
    </Inspector>
  )
}
