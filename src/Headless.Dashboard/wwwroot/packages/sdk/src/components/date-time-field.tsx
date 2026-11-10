'use no memo'

import { DateField, DateInput, DateSegment, FieldError, Label, Text, type DateValue } from 'react-aria-components'
import { useLocale } from '../locale/locale.tsx'

export interface DateTimeFieldProps {
  label: string
  description?: string
  error?: string
  isRequired?: boolean
  /**
   * Receives the typed moment as a UTC instant in ISO 8601, such as `2026-12-15T08:00:00.000Z`, or `''` once a
   * segment is cleared. The operator types it in the display time zone.
   */
  onChange: (instant: string) => void
}

const groupClass =
  'flex h-(--field-height) min-w-0 items-center rounded-md border border-input bg-deep px-2 text-foreground transition-fast focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/30 data-invalid:border-destructive'
const segmentClass =
  'rounded-sm px-px tabular-nums outline-none data-placeholder:text-faint data-[type=literal]:text-faint data-focused:bg-primary data-focused:text-primary-foreground'

/**
 * A date and time to the minute, typed in the dashboard's display time zone, which the hint names. The field holds
 * what the operator typed, so a module never parses a wall time in the browser's own zone.
 */
export function DateTimeField({ label, description, error, isRequired, onChange }: DateTimeFieldProps) {
  const { timeZone } = useLocale()

  function change(value: DateValue | null) {
    // A wall time in the display zone becomes the instant it names there; `toDate` resolves a time a DST change skips
    // or repeats the way the zone's own clocks do.
    onChange(value ? value.toDate(timeZone).toISOString() : '')
  }

  return (
    <DateField
      className="hd-field"
      granularity="minute"
      hourCycle={24}
      isInvalid={Boolean(error)}
      isRequired={isRequired}
      validationBehavior="aria"
      onChange={change}
    >
      <Label className="hd-label">{label}</Label>
      <DateInput className={groupClass}>{(segment) => <DateSegment segment={segment} className={segmentClass} />}</DateInput>
      <Text slot="description" className="hd-hint">
        {description ? `${description} ` : ''}
        {`In ${timeZone}`}
      </Text>
      <FieldError className="hd-error">{error}</FieldError>
    </DateField>
  )
}
