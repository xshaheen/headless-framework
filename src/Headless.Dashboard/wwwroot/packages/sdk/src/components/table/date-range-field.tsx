'use no memo'

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
import { CalendarIcon, NextIcon, PreviousIcon } from '../../icons/index.ts'
import { useLocale } from '../../locale/locale.tsx'
import { cn, floatingSurface } from '../cn.ts'

export interface DateRangeFieldProps {
  /** Names the field, such as `Executed`. */
  label: string
  /**
   * The chosen days as UTC instants, or `undefined` once the range is cleared. The start is the first day's midnight
   * in the display time zone and the end the midnight after the last day, so the last day is included whole.
   */
  onChange: (range: ListRange | undefined) => void
}

const groupClass =
  'flex h-(--field-height) min-w-0 items-center gap-1 rounded-md border border-input bg-deep ps-2 pe-1 text-foreground transition-fast focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/30'
const segmentClass =
  'rounded-sm px-px tabular-nums outline-none data-placeholder:text-faint data-[type=literal]:text-faint data-focused:bg-primary data-focused:text-primary-foreground'
const navClass =
  'grid cursor-pointer place-content-center rounded-md text-muted-foreground outline-none hover:bg-accent hover:text-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring'
const cellClass =
  'grid size-8 cursor-pointer place-content-center rounded-md type-small tabular-nums outline-none data-hovered:bg-accent data-outside-month:text-faint data-selected:rounded-none data-selected:bg-primary-soft data-selection-start:rounded-s-md data-selection-start:bg-primary data-selection-start:text-primary-foreground data-selection-end:rounded-e-md data-selection-end:bg-primary data-selection-end:text-primary-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring data-disabled:opacity-50'

/**
 * A list's day range, typed or picked on a calendar, read in the dashboard's display time zone. A list passes the
 * range to its keyset hook as `range`, which reads the first page again for it.
 */
export function DateRangeField({ label, onChange }: DateRangeFieldProps) {
  const { timeZone } = useLocale()

  function change(value: DateRange | null) {
    // The picker commits on every digit, so while the end year is typed (2, 20, 202) the end falls before the start;
    // such a range is never sent, and the list keeps the last whole one.
    if (value && value.end.compare(value.start) < 0) {
      return
    }

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
        <AriaButton className={`${navClass} ms-auto size-7`}>
          <CalendarIcon className="text-current" />
        </AriaButton>
      </Group>
      <Popover className={cn('z-(--z-popover) rounded-lg p-2.5', floatingSurface)}>
        <Dialog className="outline-none">
          <RangeCalendar className="flex flex-col gap-2">
            <header className="flex items-center justify-between gap-2">
              <AriaButton slot="previous" className={`${navClass} size-7`}>
                <PreviousIcon className="text-current rtl:rotate-180" />
              </AriaButton>
              <Heading className="type-small font-medium" />
              <AriaButton slot="next" className={`${navClass} size-7`}>
                <NextIcon className="text-current rtl:rotate-180" />
              </AriaButton>
            </header>
            <CalendarGrid className="border-separate border-spacing-y-0.5">{(date) => <CalendarCell date={date} className={cellClass} />}</CalendarGrid>
          </RangeCalendar>
        </Dialog>
      </Popover>
    </DateRangePicker>
  )
}
