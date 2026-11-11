'use no memo'

import { useContext, useRef, useState, type ReactNode } from 'react'
import {
  Button as AriaButton,
  CalendarCell,
  CalendarGrid,
  DateInput,
  DateRangePicker,
  DateSegment,
  Dialog,
  Group,
  GroupContext,
  Heading,
  Label,
  Popover,
  RangeCalendar,
  type ContextValue,
  type DateRange,
  type GroupProps,
} from 'react-aria-components'
import type { ListRange } from '../../data/keyset.ts'
import { CalendarIcon, NextIcon, PreviousIcon } from '../../icons/index.ts'
import { useLocale } from '../../locale/locale.tsx'
import { Button } from '../button.tsx'
import { cn, floatingSurface } from '../cn.ts'

export interface DateRangeFieldProps {
  /** Names the field, such as `Executed`. */
  label: string
  /**
   * The chosen days as UTC instants, or `undefined` once the range is cleared. The start is the first day's midnight
   * in the display time zone and the end the midnight after the last day, so the last day is included whole.
   */
  onChange: (range: ListRange | undefined) => void
  /**
   * A filter in a page header's toolbar: a chip at the control height that reads its label, or the chosen days once
   * picked, and opens a popover with the calendar over the typed start and end days, so the row holds it beside the
   * other chips. The calendar takes focus (arrow keys move, `Enter` picks); `Tab` reaches the typed days.
   */
  compact?: boolean
}

const groupClass =
  'flex h-(--field-height) min-w-0 items-center gap-1 rounded-md border border-input bg-deep ps-2 text-foreground transition-fast focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/30'
const segmentClass =
  'rounded-sm px-px tabular-nums outline-none data-placeholder:text-faint data-[type=literal]:text-faint data-focused:bg-primary data-focused:text-primary-foreground'
const navClass =
  'grid cursor-pointer place-content-center rounded-md text-muted-foreground outline-none hover:bg-accent hover:text-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring'
const cellClass =
  'grid size-8 cursor-pointer place-content-center rounded-md type-small tabular-nums outline-none data-hovered:bg-accent data-outside-month:text-faint data-selected:rounded-none data-selected:bg-primary-soft data-selection-start:rounded-s-md data-selection-start:bg-primary data-selection-start:text-primary-foreground data-selection-end:rounded-e-md data-selection-end:bg-primary data-selection-end:text-primary-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring data-disabled:opacity-50'
// The chip matches `FilterSelect`: off it reads its label, set it takes the pressed state and reads the days.
const chipClass =
  'inline-flex h-(--control-height) cursor-pointer items-center gap-1.5 rounded-md border border-border bg-transparent px-2 type-small whitespace-nowrap text-muted-foreground transition-fast outline-none hover:border-edge hover:text-foreground data-focus-visible:outline-solid data-focus-visible:outline-2 data-focus-visible:outline-offset-2 data-focus-visible:outline-ring data-[set]:border-primary data-[set]:bg-primary-soft data-[set]:text-foreground'

/**
 * A list's day range, typed or picked on a calendar, read in the dashboard's display time zone. A list passes the
 * range to its keyset hook as `range`, which reads the first page again for it.
 */
export function DateRangeField({ label, onChange, compact }: DateRangeFieldProps) {
  const { locale, timeZone } = useLocale()
  const [value, setValue] = useState<DateRange | null>(null)
  const chip = useRef<HTMLButtonElement>(null)

  function change(next: DateRange | null) {
    setValue(next)

    // The picker commits on every digit, so while the end year is typed (2, 20, 202) the end falls before the start;
    // such a range is never sent, and the list keeps the last whole one.
    if (next && next.end.compare(next.start) < 0) {
      return
    }

    // The picker reads whole days, so the end is the next day's midnight and the server compares it exclusively.
    onChange(
      next
        ? { start: next.start.toDate(timeZone).toISOString(), end: next.end.add({ days: 1 }).toDate(timeZone).toISOString() }
        : undefined,
    )
  }

  const calendar = (
    <RangeCalendar className="flex flex-col gap-2">
      <header className="flex items-center justify-between gap-2">
        <AriaButton slot="previous" className={`${navClass} size-7`}>
          <PreviousIcon className="text-current rtl:rotate-180" />
        </AriaButton>
        <Heading className="type-small-medium" />
        <AriaButton slot="next" className={`${navClass} size-7`}>
          <NextIcon className="text-current rtl:rotate-180" />
        </AriaButton>
      </header>
      <CalendarGrid className="border-separate border-spacing-y-0.5">{(date) => <CalendarCell date={date} className={cellClass} />}</CalendarGrid>
    </RangeCalendar>
  )

  // The typed start and end days. The picker hands each its field props through context, which its popover keeps, so
  // the compact chip's popover holds the same inputs as the full field.
  const inputs = (
    <>
      <DateInput slot="start" className="flex">
        {(segment) => <DateSegment segment={segment} className={segmentClass} />}
      </DateInput>
      <span aria-hidden="true" className="text-faint">
        –
      </span>
      <DateInput slot="end" className="flex">
        {(segment) => <DateSegment segment={segment} className={segmentClass} />}
      </DateInput>
    </>
  )

  if (compact) {
    const days = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric' })
    const reading = value ? `${days.format(value.start.toDate(timeZone))} – ${days.format(value.end.toDate(timeZone))}` : undefined

    // The picker closes its popover on every range change by default; it stays open until Esc or a press outside,
    // so the operator sees the picked days and can clear them in the same place. The picker also anchors its popover
    // to its group, and moves the typing from the start day on to the end day inside that group. The chip is no group
    // and the popover clears the group context, so the chip anchors the popover (without it the popover never shows)
    // and the typed days take the picker's group, read outside the popover and given again inside it.
    return (
      <DateRangePicker className="inline-flex min-w-0" granularity="day" value={value} onChange={change} shouldCloseOnSelect={false}>
        <Label className="sr-only">{label}</Label>
        <AriaButton ref={chip} className={chipClass} data-set={value ? '' : undefined}>
          <CalendarIcon className="size-(--icon-sm) text-current" />
          {reading ? (
            <>
              <span className="text-faint">{label}</span>
              <span className="tabular-nums">{reading}</span>
            </>
          ) : (
            label
          )}
        </AriaButton>
        <PickerGroup>
          {(group) => (
            <Popover triggerRef={chip} className={cn('z-(--z-popover) rounded-lg p-2.5', floatingSurface)}>
              <Dialog className="flex flex-col gap-2.5 outline-none">
                {calendar}
                {/* After the calendar in tab order, so its arrow keys stay where focus lands and `Tab` moves on to typing. */}
                <GroupContext value={group}>
                  <Group className={`${groupClass} pe-2`}>{inputs}</Group>
                </GroupContext>
                {value ? (
                  <Button variant="ghost" onPress={() => change(null)}>
                    Clear days
                  </Button>
                ) : null}
              </Dialog>
            </Popover>
          )}
        </PickerGroup>
      </DateRangePicker>
    )
  }

  return (
    <DateRangePicker className="flex min-w-0 flex-col gap-1" granularity="day" value={value} onChange={change}>
      <Label className="hd-label">{label}</Label>
      <Group className={`${groupClass} pe-1`}>
        {inputs}
        <AriaButton className={`${navClass} ms-auto size-7`}>
          <CalendarIcon className="text-current" />
        </AriaButton>
      </Group>
      <Popover className={cn('z-(--z-popover) rounded-lg p-2.5', floatingSurface)}>
        <Dialog className="outline-none">{calendar}</Dialog>
      </Popover>
    </DateRangePicker>
  )
}

/** Hands its children the enclosing picker's group context, which a popover inside the picker no longer sees. */
function PickerGroup({ children }: { children: (group: ContextValue<GroupProps, HTMLDivElement>) => ReactNode }) {
  return children(useContext(GroupContext))
}
