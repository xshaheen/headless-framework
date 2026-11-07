import { useId, useState, type ReactNode } from 'react'
import { Button } from '../components/button.tsx'
import { Dialog } from '../components/dialog.tsx'
import { TextInputField } from '../components/form.tsx'
import type { ArgumentSchema, Confirmation, ObjectSummary } from './api.ts'
import { checkArguments, SchemaFields, type ArgumentValues } from './schema-form.tsx'

export interface ConfirmedAction {
  reason?: string
  typedCount?: number
  arguments: Record<string, string | number | boolean>
}

export interface ActionConfirmProps {
  isOpen: boolean
  onCancel: () => void
  onConfirm: (action: ConfirmedAction) => void
  /** The consequence, such as `Requeue 312 failed runs of billing.charge`. */
  title: string
  /** The reach, stated before it runs. */
  reach?: ReactNode
  /** The outcome the confirm button names: `Requeue 312 runs`, never Yes. */
  confirmLabel: string
  /** The outcome of backing out: `Keep runs as they are`. */
  cancelLabel?: string
  confirmation: Confirmation
  /** The row count a typed confirmation asks for. */
  count?: number
  /** Whether a reason is asked for, and whether it must be given. */
  reason?: 'none' | 'optional' | 'required'
  sample?: readonly ObjectSummary[]
  schema?: ArgumentSchema
  /** Whether the call is in flight: the buttons wait. */
  isBusy?: boolean
  /** The server's refusal, shown in place, so it never lives only in a toast. */
  error?: string
  tone?: 'danger'
}

/**
 * The confirmation of an action: its consequence and reach, the reason where one is recorded, the arguments, and
 * outcome-labelled buttons. A typed confirmation keeps the confirm button disabled until the typed count matches.
 */
export function ActionConfirm(props: ActionConfirmProps) {
  return props.isOpen ? <ActionConfirmDialog {...props} /> : null
}

function ActionConfirmDialog({
  onCancel,
  onConfirm,
  title,
  reach,
  confirmLabel,
  cancelLabel = 'Cancel',
  confirmation,
  count = 0,
  reason = 'none',
  sample,
  schema,
  isBusy,
  error,
  tone,
}: ActionConfirmProps) {
  const formId = useId()
  const [reasonText, setReasonText] = useState('')
  const [typed, setTyped] = useState('')
  const [values, setValues] = useState<ArgumentValues>({})
  const [errors, setErrors] = useState<Record<string, string>>({})

  const typedMatches = confirmation !== 'typed' || typed.trim() === String(count)
  const reasonGiven = reason !== 'required' || reasonText.trim() !== ''
  const blocked = !typedMatches || !reasonGiven || Boolean(isBusy)

  return (
    <Dialog isOpen onOpenChange={(open) => (open ? undefined : onCancel())} title={title} isDismissable={!isBusy}>
      <form
        id={formId}
        noValidate
        className="hd-fields"
        onSubmit={(event) => {
          event.preventDefault()

          if (blocked) {
            return
          }

          const checked = schema ? checkArguments(schema, values) : { ok: true as const, arguments: {} }

          if (!checked.ok) {
            setErrors(checked.errors)
            return
          }

          setErrors({})
          onConfirm({
            arguments: checked.arguments,
            reason: reasonText.trim() || undefined,
            typedCount: confirmation === 'typed' ? count : undefined,
          })
        }}
      >
        {reach ? <div className="hd-hint">{reach}</div> : null}
        {sample && sample.length > 0 ? (
          <ul className="hd-sample" aria-label="Sample of the rows in scope">
            {sample.map((row) => (
              <li key={`${row.target.kind}:${row.target.id}`}>
                <span>{row.title}</span>
                <span className="hd-hint">{row.status}</span>
              </li>
            ))}
          </ul>
        ) : null}
        {schema && Object.keys(schema.properties).length > 0 ? (
          <SchemaFields
            schema={schema}
            values={values}
            errors={errors}
            onChange={(name, value) => setValues((current) => ({ ...current, [name]: value }))}
          />
        ) : null}
        {reason !== 'none' ? (
          <TextInputField
            label={reason === 'required' ? 'Reason' : 'Reason (optional)'}
            description="Recorded in the operator log."
            value={reasonText}
            onChange={setReasonText}
            isRequired={reason === 'required'}
            maxLength={1000}
            multiline
          />
        ) : null}
        {confirmation === 'typed' ? (
          <TextInputField
            label={`Type ${count} to confirm`}
            value={typed}
            onChange={setTyped}
            inputMode="numeric"
            isRequired
          />
        ) : null}
        {error ? (
          <p role="alert" className="hd-error">
            {error}
          </p>
        ) : null}
        <div className="hd-dialog-actions">
          <Button onPress={onCancel} isDisabled={isBusy}>
            {cancelLabel}
          </Button>
          <Button type="submit" variant={tone === 'danger' ? 'danger' : 'primary'} isDisabled={blocked}>
            {confirmLabel}
          </Button>
        </div>
      </form>
    </Dialog>
  )
}
