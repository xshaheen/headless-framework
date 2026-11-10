// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useId, type ReactNode } from 'react'
import { Checkbox, FieldError, Input, Label, Text, TextArea, TextField } from 'react-aria-components'
import { Text as ToneText } from './display/text.tsx'

/**
 * shadcn's input: 30 px on the field edge, which reaches 3:1 on its surface. Focus swaps the border to the accent and
 * adds a soft ring of it; an invalid field takes the danger tone instead.
 */
export const inputClass =
  'h-(--field-height) w-full min-w-0 rounded-md border border-input bg-deep px-2 text-foreground transition-fast outline-none placeholder:text-faint focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 data-focused:border-ring aria-invalid:border-destructive aria-invalid:ring-destructive/20 data-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50'

/**
 * The same input at the control height, for the search in a page header's toolbar: it lines up with the chips and
 * buttons beside it, and its label is read, not shown, because its placeholder names it.
 */
export const compactInputClass = `${inputClass} h-(--control-height) type-small`

/**
 * How wide a field is in a form, by what it holds: `short` for a number or a few characters (`12ch`), `medium` for a
 * name, a zone, or a time (`32ch`), and `full`, the default, for a sentence or JSON (the form's measure).
 */
export type FieldWidth = 'short' | 'medium' | 'full'

const widthClasses: Readonly<Record<FieldWidth, string>> = {
  short: 'hd-field max-w-[12ch]',
  medium: 'hd-field max-w-[32ch]',
  full: 'hd-field',
}

interface FieldBase {
  label: string
  description?: string
  error?: string
  isRequired?: boolean
  /** How wide the field is, by what it holds; see `FieldWidth`. */
  width?: FieldWidth
}

export interface TextInputFieldProps extends FieldBase {
  value: string
  onChange: (value: string) => void
  /** `numeric` and `decimal` bring up a number keypad on phones. */
  inputMode?: 'text' | 'numeric' | 'decimal'
  maxLength?: number
  multiline?: boolean
  /** Code typed as text, such as JSON: monospace, left to right, and never spell-checked. Applies to `multiline` only. */
  code?: boolean
  name?: string
}

/** A labelled text field with its hint and inline error. */
export function TextInputField({ label, description, error, isRequired, width = 'full', value, onChange, inputMode, maxLength, multiline, code, name }: TextInputFieldProps) {
  return (
    <TextField
      className={widthClasses[width]}
      value={value}
      onChange={onChange}
      isInvalid={Boolean(error)}
      isRequired={isRequired}
      name={name}
      validationBehavior="aria"
    >
      <Label className="hd-label">{label}</Label>
      {multiline ? (
        <TextArea
          className={`${inputClass} h-auto min-h-(--textarea-min) resize-y py-2${code ? ' hd-mono' : ''}`}
          maxLength={maxLength}
          spellCheck={code ? false : undefined}
        />
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
  /** What an optional field's empty choice reads: `None` by default. */
  emptyLabel?: string
}

/**
 * A labelled choice from a short list in a form, as the platform's own select, which every device handles well. A
 * list's filter is a `FilterSelect`, a chip in the page header's toolbar.
 */
export function SelectField({ label, description, error, isRequired, width = 'full', value, onChange, options, emptyLabel = 'None' }: SelectFieldProps) {
  const id = useId()

  return (
    <div className={widthClasses[width]}>
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
      {error ? <ToneText tone="error">{error}</ToneText> : null}
    </div>
  )
}

/** Groups a form's fields with the dashboard's spacing. */
export function FieldGroup({ children }: { children: ReactNode }) {
  return <div className="hd-fields">{children}</div>
}
