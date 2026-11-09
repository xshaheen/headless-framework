// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useId, type ReactNode } from 'react'
import { Checkbox, FieldError, Input, Label, Text, TextArea, TextField } from 'react-aria-components'

/**
 * shadcn's input: 30 px on the field edge, which reaches 3:1 on its surface. Focus swaps the border to the accent and
 * adds a soft ring of it; an invalid field takes the danger tone instead.
 */
export const inputClass =
  'h-(--field-height) w-full min-w-0 rounded-md border border-input bg-deep px-2 text-foreground transition-fast outline-none placeholder:text-faint focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 data-focused:border-ring aria-invalid:border-destructive aria-invalid:ring-destructive/20 data-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50'

interface FieldBase {
  label: string
  description?: string
  error?: string
  isRequired?: boolean
}

export interface TextInputFieldProps extends FieldBase {
  value: string
  onChange: (value: string) => void
  /** `numeric` and `decimal` bring up a number keypad on phones. */
  inputMode?: 'text' | 'numeric' | 'decimal'
  maxLength?: number
  multiline?: boolean
  name?: string
}

/** A labelled text field with its hint and inline error. */
export function TextInputField({ label, description, error, isRequired, value, onChange, inputMode, maxLength, multiline, name }: TextInputFieldProps) {
  return (
    <TextField
      className="hd-field"
      value={value}
      onChange={onChange}
      isInvalid={Boolean(error)}
      isRequired={isRequired}
      name={name}
      validationBehavior="aria"
    >
      <Label className="hd-label">{label}</Label>
      {multiline ? (
        <TextArea className={`${inputClass} h-auto min-h-(--textarea-min) resize-y py-2`} maxLength={maxLength} />
      ) : (
        <Input className={inputClass} inputMode={inputMode} maxLength={maxLength} />
      )}
      {description ? (
        <Text slot="description" className="hd-hint">
          {description}
        </Text>
      ) : null}
      <FieldError className="hd-error">{error}</FieldError>
    </TextField>
  )
}

export interface SelectFieldProps extends FieldBase {
  value: string
  onChange: (value: string) => void
  options: readonly { value: string; label: string }[]
  /** What an optional field's empty choice reads: `None` by default, `Any status` for a filter. */
  emptyLabel?: string
}

/** A labelled choice from a short list, as the platform's own select, which every device handles well. */
export function SelectField({ label, description, error, isRequired, value, onChange, options, emptyLabel = 'None' }: SelectFieldProps) {
  const id = useId()

  return (
    <div className="hd-field">
      <label className="hd-label" htmlFor={`${id}-input`}>
        {label}
      </label>
      <select
        id={`${id}-input`}
        className={inputClass}
        value={value}
        required={isRequired}
        aria-invalid={error ? true : undefined}
        aria-describedby={[description ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined}
        onChange={(event) => onChange(event.currentTarget.value)}
      >
        {isRequired ? null : <option value="">{emptyLabel}</option>}
        {isRequired && value === '' ? <option value="">Choose…</option> : null}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      {description ? (
        <span id={`${id}-hint`} className="hd-hint">
          {description}
        </span>
      ) : null}
      {error ? (
        <span id={`${id}-error`} className="hd-error">
          {error}
        </span>
      ) : null}
    </div>
  )
}

export interface CheckboxFieldProps {
  label: string
  value: boolean
  onChange: (value: boolean) => void
  error?: string
}

export function CheckboxField({ label, value, onChange, error }: CheckboxFieldProps) {
  return (
    <div className="hd-field">
      <Checkbox className="group inline-flex cursor-pointer items-center gap-2" isSelected={value} onChange={onChange} isInvalid={Boolean(error)}>
        <span
          className="grid size-4 place-content-center rounded-sm border border-input bg-card text-primary-foreground transition-fast group-data-focus-visible:outline-2 group-data-focus-visible:outline-offset-2 group-data-focus-visible:outline-ring group-data-invalid:border-destructive group-data-selected:border-primary group-data-selected:bg-primary"
          aria-hidden="true"
        >
          <svg viewBox="0 0 16 16" className="hidden size-3 fill-none stroke-current stroke-2 group-data-selected:block">
            <path d="M3.5 8.5l3 3 6-7" />
          </svg>
        </span>
        {label}
      </Checkbox>
      {error ? <span className="hd-error">{error}</span> : null}
    </div>
  )
}

/** Groups a form's fields with the dashboard's spacing. */
export function FieldGroup({ children }: { children: ReactNode }) {
  return <div className="hd-fields">{children}</div>
}
