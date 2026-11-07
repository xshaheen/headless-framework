import { useId, type ReactNode } from 'react'
import { Checkbox, FieldError, Input, Label, Text, TextArea, TextField } from 'react-aria-components'

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
        <TextArea className="hd-input hd-textarea" maxLength={maxLength} />
      ) : (
        <Input className="hd-input" inputMode={inputMode} maxLength={maxLength} />
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
        className="hd-input"
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
      <Checkbox className="hd-checkbox" isSelected={value} onChange={onChange} isInvalid={Boolean(error)}>
        <span className="hd-checkbox-box" aria-hidden="true" />
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
