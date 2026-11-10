'use no memo'

import { useContext, useEffect, useRef } from 'react'
import { DateField, DateFieldStateContext, DateInput, DateSegment, FieldError, Label, Text } from 'react-aria-components'
import { useLocale } from '../locale/locale.tsx'

export interface DateTimeFieldProps {
  label: string
  description?: string
  error?: string
  isRequired?: boolean
  /**
   * Receives the typed moment as a UTC instant in ISO 8601, such as `2026-12-15T08:00:00.000Z`; `''` while no segment
   * holds a value; or {@link incompleteDateTime} while some segments do and others do not. The operator types it in the
   * display time zone.
   */
  onChange: (instant: string) => void
}

/**
 * What the field reports while only some segments are typed: not empty, so an optional argument is not taken as
 * omitted, and not a date, so the form refuses it as incomplete instead of sending the call without it.
 */
export const incompleteDateTime = 'incomplete'

const groupClass =
  'flex h-(--field-height) min-w-0 items-center rounded-md border border-input bg-deep px-2 text-foreground transition-fast focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/30 data-invalid:border-destructive'
const segmentClass =
  'rounded-sm px-px tabular-nums outline-none data-placeholder:text-faint data-[type=literal]:text-faint data-focused:bg-primary data-focused:text-primary-foreground'

/**
 * A date and time to the minute, typed in the dashboard's display time zone, which the hint names. The field holds
 * what the operator typed, so a module never parses a wall time in the browser's own zone.
 */
export function DateTimeField({ label, description, error, isRequired, onChange }: DateTimeFieldProps) {
  return (
    <DateField
      className="hd-field"
      granularity="minute"
      hourCycle={24}
      isInvalid={Boolean(error)}
      isRequired={isRequired}
      validationBehavior="aria"
    >
      <Label className="hd-label">{label}</Label>
      <DateInput className={groupClass}>{(segment) => <DateSegment segment={segment} className={segmentClass} />}</DateInput>
      <Report onChange={onChange} />
      <TimeZoneHint description={description} />
      <FieldError className="hd-error">{error}</FieldError>
    </DateField>
  )
}

function TimeZoneHint({ description }: { description: string | undefined }) {
  const { timeZone } = useLocale()

  return (
    <Text slot="description" className="hd-hint">
      {description ? `${description} ` : ''}
      {`In ${timeZone}`}
    </Text>
  )
}

/**
 * Reports the field from its segments rather than from `DateField`'s `onChange`, which fires only when the whole value
 * appears or goes, so a date typed without its time would read as never touched.
 */
function Report({ onChange }: { onChange: (instant: string) => void }) {
  const { timeZone } = useLocale()
  const state = useContext(DateFieldStateContext)
  const reported = useRef('')
  let current = ''

  if (state?.value) {
    // A wall time in the display zone becomes the instant it names there; `toDate` resolves a time a DST change skips
    // or repeats the way the zone's own clocks do.
    current = state.value.toDate(timeZone).toISOString()
  } else if (state?.segments.some((segment) => segment.isEditable && !segment.isPlaceholder)) {
    current = incompleteDateTime
  }

  useEffect(() => {
    if (current !== reported.current) {
      reported.current = current
      onChange(current)
    }
  })

  return null
}
