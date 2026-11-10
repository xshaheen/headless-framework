import { lazy, Suspense, useId, useState } from 'react'
import { Button } from '../components/button.tsx'
import { CheckboxField, FieldGroup, SelectField, TextInputField } from '../components/form.tsx'
import { formatJson, PayloadView } from '../components/payload/payload-view.tsx'
import type { ArgumentProperty, ArgumentSchema } from './api.ts'

/**
 * A form's raw values: text for text, number, and JSON fields, `true`/`false` for toggles, and for date-times a UTC ISO
 * instant, `''` when untouched, or a non-date marker when only partly typed, which the check refuses as incomplete.
 */
export type ArgumentValues = Readonly<Record<string, string | boolean>>

export type ArgumentCheck =
  | { ok: true; arguments: Record<string, string | number | boolean> }
  | { ok: false; errors: Record<string, string> }

// React Aria's date code would otherwise join the shell's first load, which every view pays for; it loads with the first
// form that asks for a date-time, from the chunk the date-range entry shares.
const DateTimeField = lazy(async () => ({ default: (await import('../components/date-time-field.tsx')).DateTimeField }))

function checkOne(property: ArgumentProperty, raw: string | boolean | undefined, required: boolean) {
  if (property.type === 'boolean') {
    return { value: raw === true }
  }

  const text = typeof raw === 'string' ? raw.trim() : ''

  if (text === '') {
    return required ? { error: `${property.title} is required.` } : {}
  }

  if (property.enum) {
    const match = property.enum.find((candidate) => String(candidate) === text)

    return match === undefined ? { error: `Choose one of the listed values for ${property.title}.` } : { value: match }
  }

  if (property.type === 'integer' || property.type === 'number') {
    const value = Number(text)

    if (!Number.isFinite(value) || (property.type === 'integer' && !Number.isInteger(value))) {
      return { error: `${property.title} must be a ${property.type === 'integer' ? 'whole number' : 'number'}.` }
    }

    if (property.minimum !== undefined && value < property.minimum) {
      return { error: `${property.title} must be at least ${property.minimum}.` }
    }

    if (property.maximum !== undefined && value > property.maximum) {
      return { error: `${property.title} must be at most ${property.maximum}.` }
    }

    return { value }
  }

  if (property.format === 'date-time') {
    const instant = Date.parse(text)

    if (Number.isNaN(instant)) {
      return { error: `${property.title} must be a complete date and time.` }
    }

    // The server checks again with its own clock, so a moment that passes while the form is open is still refused.
    if (property.notBeforeNow && instant < Date.now()) {
      return { error: `${property.title} must not be in the past.` }
    }

    return { value: text }
  }

  if (property.maxLength !== undefined && text.length > property.maxLength) {
    return { error: `${property.title} must be at most ${property.maxLength} characters.` }
  }

  if (property.contentMediaType === 'application/json' && formatJson(text) === null) {
    return { error: `${property.title} must be valid JSON.` }
  }

  return { value: text }
}

/**
 * Checks form values against the schema the server will apply, so a call it would refuse is never sent: required,
 * type, enum, minimum, maximum, maxLength, a whole date-time not in the past, and JSON that parses.
 */
export function checkArguments(schema: ArgumentSchema, values: ArgumentValues): ArgumentCheck {
  const errors: Record<string, string> = {}
  const result: Record<string, string | number | boolean> = {}

  for (const [name, property] of Object.entries(schema.properties)) {
    const checked = checkOne(property, values[name], schema.required.includes(name))

    if ('error' in checked && checked.error) {
      errors[name] = checked.error
    } else if ('value' in checked && checked.value !== undefined) {
      result[name] = checked.value
    }
  }

  return Object.keys(errors).length > 0 ? { ok: false, errors } : { ok: true, arguments: result }
}

export interface SchemaFieldsProps {
  schema: ArgumentSchema
  values: ArgumentValues
  onChange: (name: string, value: string | boolean) => void
  errors?: Readonly<Record<string, string>>
}

/** Renders a command's arguments as fields, in the order the command declares them. */
export function SchemaFields({ schema, values, onChange, errors = {} }: SchemaFieldsProps) {
  return (
    <FieldGroup>
      {Object.entries(schema.properties).map(([name, property]) => {
        const required = schema.required.includes(name)
        const error = errors[name]
        const raw = values[name]

        if (property.type === 'boolean') {
          return <CheckboxField key={name} label={property.title} value={raw === true} onChange={(value) => onChange(name, value)} error={error} />
        }

        const text = typeof raw === 'string' ? raw : ''

        if (property.enum) {
          return (
            <SelectField
              key={name}
              label={property.title}
              value={text}
              isRequired={required}
              error={error}
              onChange={(value) => onChange(name, value)}
              options={property.enum.map((option) => ({ value: String(option), label: String(option) }))}
            />
          )
        }

        if (property.format === 'date-time') {
          return (
            <Suspense key={name} fallback={null}>
              <DateTimeField
                label={property.title}
                isRequired={required}
                error={error}
                description={property.notBeforeNow ? 'Not in the past.' : undefined}
                onChange={(value) => onChange(name, value)}
              />
            </Suspense>
          )
        }

        if (property.contentMediaType === 'application/json') {
          return <JsonField key={name} property={property} required={required} error={error} text={text} onChange={(value) => onChange(name, value)} />
        }

        const numeric = property.type === 'integer' || property.type === 'number'
        const range =
          numeric && (property.minimum !== undefined || property.maximum !== undefined)
            ? `From ${property.minimum ?? '−∞'} to ${property.maximum ?? '∞'}`
            : undefined

        return (
          <TextInputField
            key={name}
            label={property.title}
            value={text}
            isRequired={required}
            error={error}
            description={range}
            inputMode={property.type === 'integer' ? 'numeric' : property.type === 'number' ? 'decimal' : 'text'}
            maxLength={property.maxLength}
            onChange={(value) => onChange(name, value)}
          />
        )
      })}
    </FieldGroup>
  )
}

interface JsonFieldProps {
  property: ArgumentProperty
  required: boolean
  error: string | undefined
  text: string
  onChange: (value: string) => void
}

/** JSON typed as text, with the payload view's colours shown under it once the text parses. */
function JsonField({ property, required, error, text, onChange }: JsonFieldProps) {
  const parses = text.trim() !== '' && formatJson(text) !== null

  return (
    <div className="flex flex-col gap-2">
      <TextInputField
        label={property.title}
        value={text}
        isRequired={required}
        error={error}
        description={text.trim() === '' || parses ? 'JSON' : 'Not valid JSON yet'}
        maxLength={property.maxLength}
        multiline
        code
        onChange={onChange}
      />
      {parses ? <PayloadView content={text} contentType="application/json" /> : null}
    </div>
  )
}

export interface SchemaFormProps {
  schema: ArgumentSchema
  /** Names the outcome, such as `Requeue run`. */
  submitLabel: string
  onSubmit: (args: Record<string, string | number | boolean>) => void
}

/** A command's argument form on its own: submit stays blocked, with each violation shown on its field. */
export function SchemaForm({ schema, submitLabel, onSubmit }: SchemaFormProps) {
  const id = useId()
  const [values, setValues] = useState<ArgumentValues>({})
  const [errors, setErrors] = useState<Record<string, string>>({})

  return (
    <form
      id={id}
      noValidate
      className="hd-fields"
      onSubmit={(event) => {
        event.preventDefault()
        const checked = checkArguments(schema, values)

        if (checked.ok) {
          setErrors({})
          onSubmit(checked.arguments)
        } else {
          setErrors(checked.errors)
        }
      }}
    >
      <SchemaFields
        schema={schema}
        values={values}
        errors={errors}
        onChange={(name, value) => setValues((current) => ({ ...current, [name]: value }))}
      />
      <div className="hd-actions">
        <Button type="submit" variant="primary">
          {submitLabel}
        </Button>
      </div>
    </form>
  )
}
