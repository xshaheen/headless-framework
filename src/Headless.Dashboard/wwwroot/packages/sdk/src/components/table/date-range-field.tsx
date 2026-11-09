import {
  Button as AriaButton,
  CalendarCell,
  CalendarGrid,
  DateInput,
  DateRangePicker,
  DateSegment,
  Dialog,
  Group,
  Heading,
  Label,
  Popover,
  RangeCalendar,
  type DateRange,
} from 'react-aria-components'
import type { ListRange } from '../../data/keyset.ts'
import { useLocale } from '../../locale/locale.tsx'

export interface DateRangeFieldProps {
  /** Names the field, such as `Executed`. */
  label: string
  /**
   * The chosen days as UTC instants, or `undefined` once the range is cleared. The start is the first day's midnight
   * in the display time zone and the end the midnight after the last day, so the last day is included whole.
   */
  onChange: (range: ListRange | undefined) => void
}

// The segments sit in one field-height box; a focused segment takes the accent, as a focused input does.
const groupClass =
  'flex h-(--field-height) min-w-0 items-center gap-1 rounded-md border border-input bg-deep ps-2 pe-1 text-foreground transition-fast focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/30'
const segmentClass =
  'rounded-sm px-px tabular-nums outline-none data-placeholder:text-faint data-[type=literal]:text-faint data-focused:bg-primary data-focused:text-primary-foreground'
const navClass =
  'grid size-7 cursor-pointer place-content-center rounded-md text-muted-foreground outline-none hover:bg-accent hover:text-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring'
// The range reads as a soft band, its two ends in the accent; outside days and unavailable ones fade.
const cellClass =
  'grid size-8 cursor-pointer place-content-center rounded-md type-small tabular-nums outline-none data-hovered:bg-accent data-outside-month:text-faint data-selected:rounded-none data-selected:bg-primary-soft data-selection-start:rounded-s-md data-selection-start:bg-primary data-selection-start:text-primary-foreground data-selection-end:rounded-e-md data-selection-end:bg-primary data-selection-end:text-primary-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring data-disabled:opacity-50'

/**
 * A list's day range, typed or picked on a calendar, read in the dashboard's display time zone. A list passes the
 * range to its keyset hook as `range`, which reads the first page again for it.
 */
export function DateRangeField({ label, onChange }: DateRangeFieldProps) {
  const { timeZone } = useLocale()

  function change(value: DateRange | null) {
    // The picker reads whole days, so the end is the next day's midnight and the server compares it exclusively.
    onChange(
      value
        ? { start: value.start.toDate(timeZone).toISOString(), end: value.end.add({ days: 1 }).toDate(timeZone).toISOString() }
        : undefined,
    )
  }

  return (
    <DateRangePicker className="flex min-w-0 flex-col gap-1" granularity="day" onChange={change}>
      <Label className="hd-label">{label}</Label>
      <Group className={groupClass}>
        <DateInput slot="start" className="flex">
          {(segment) => <DateSegment segment={segment} className={segmentClass} />}
        </DateInput>
        <span aria-hidden="true" className="text-faint">
          –
        </span>
        <DateInput slot="end" className="flex">
          {(segment) => <DateSegment segment={segment} className={segmentClass} />}
        </DateInput>
        <AriaButton className={`${navClass} ms-auto size-6`}>
          <svg viewBox="0 0 16 16" aria-hidden="true" className="size-3.5 fill-none stroke-current stroke-[1.5]">
            <rect x="2.5" y="3.5" width="11" height="10" rx="1.5" />
            <path d="M2.5 6.5h11M5.5 2v3M10.5 2v3" />
          </svg>
        </AriaButton>
      </Group>
      <Popover className="z-(--z-popover) rounded-lg border border-border bg-popover p-2.5 text-popover-foreground shadow-lg">
        <Dialog className="outline-none">
          <RangeCalendar className="flex flex-col gap-2">
            <header className="flex items-center justify-between gap-2">
              <AriaButton slot="previous" className={navClass}>
                <span aria-hidden="true" className="rtl:rotate-180">
                  ‹
                </span>
              </AriaButton>
              <Heading className="type-small font-medium" />
              <AriaButton slot="next" className={navClass}>
                <span aria-hidden="true" className="rtl:rotate-180">
                  ›
                </span>
              </AriaButton>
            </header>
            <CalendarGrid className="border-separate border-spacing-y-0.5">{(date) => <CalendarCell date={date} className={cellClass} />}</CalendarGrid>
          </RangeCalendar>
        </Dialog>
      </Popover>
    </DateRangePicker>
  )
}
