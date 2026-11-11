'use no memo'

import { isSameDay } from '@internationalized/date'
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
import { calendarDayOf, parseCalendarDay, useToday } from '../../date-range/calendar-date.ts'
import { toCustomTimeRange, type TimeRange, type TimeRangePreset } from '../../date-range/time-range.ts'
import { CalendarIcon, NextIcon, PreviousIcon } from '../../icons/index.ts'
import { useLocale } from '../../locale/locale.tsx'
import { Button } from '../button.tsx'
import { cn, floatingSurface } from '../cn.ts'
import { SegmentedControl } from '../display/segmented-control.tsx'

export interface DateRangeFieldProps {
  /** Names the field, such as `Executed`. */
  label: string
  /**
   * The range shown: a preset window or two ISO days, or `undefined` for none. `useTimeRangeSearch` keeps it in the
   * URL, and `useListRange` turns it into the instants a keyset list reads.
   */
  value: TimeRange | undefined
  /** Receives a picked preset, the picked days once both are whole and in order, or `undefined` once cleared. */
  onChange: (range: TimeRange | undefined) => void
  /**
   * The rolling windows offered above the days, such as `timeRangePresets` (15 min to 30 days), with a `Custom` choice
   * that opens the days the current preset covers. Without them the field picks days only.
   */
  presets?: readonly TimeRangePreset[]
  /** The earliest and latest pickable ISO days. An earliest after the latest leaves every day disabled. */
  min?: string
  max?: string
  /**
   * A filter in a page header's toolbar: a chip at the control height that reads its label, or the chosen range once
   * set, and opens a popover with the presets and the calendar over the typed start and end days, so the row holds it
   * beside the other chips. The calendar takes focus (arrow keys move, `Enter` picks); `Tab` reaches the typed days.
   */
  compact?: boolean
}

const custom = 'custom'

const groupClass =
  'flex h-(--field-height) min-w-0 items-center gap-1 rounded-md border border-input bg-deep ps-2 text-foreground transition-fast focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/30'
const segmentClass =
  'rounded-sm px-px tabular-nums outline-none data-placeholder:text-faint data-[type=literal]:text-faint data-focused:bg-primary data-focused:text-primary-foreground'
const navClass =
  'grid cursor-pointer place-content-center rounded-md text-muted-foreground outline-none hover:bg-accent hover:text-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring'
const cellClass =
  'grid size-8 cursor-pointer place-content-center rounded-md type-small tabular-nums outline-none data-hovered:bg-accent data-outside-month:text-faint data-selected:rounded-none data-selected:bg-primary-soft data-selection-start:rounded-s-md data-selection-start:bg-primary data-selection-start:text-primary-foreground data-selection-end:rounded-e-md data-selection-end:bg-primary data-selection-end:text-primary-foreground data-focus-visible:ring-2 data-focus-visible:ring-ring data-disabled:cursor-default data-disabled:opacity-50 data-zone-today:relative data-zone-today:font-semibold data-zone-today:after:absolute data-zone-today:after:inset-x-0 data-zone-today:after:bottom-1 data-zone-today:after:mx-auto data-zone-today:after:size-1 data-zone-today:after:rounded-full data-zone-today:after:bg-current'
// The chip matches `FilterSelect`: off it reads its label, set it takes the pressed state and reads the range.
const chipClass =
  'inline-flex h-(--control-height) cursor-pointer items-center gap-1.5 rounded-md border border-border bg-transparent px-2 type-small whitespace-nowrap text-muted-foreground transition-fast outline-none hover:border-edge hover:text-foreground data-focus-visible:outline-solid data-focus-visible:outline-2 data-focus-visible:outline-offset-2 data-focus-visible:outline-ring data-[set]:border-primary data-[set]:bg-primary-soft data-[set]:text-foreground'

function daysOf(range: TimeRange | undefined): DateRange | null {
  const start = range?.kind === 'custom' ? parseCalendarDay(range.start) : null
  const end = range?.kind === 'custom' ? parseCalendarDay(range.end) : null

  return start && end ? { start, end } : null
}

function keyOf(range: TimeRange | undefined) {
  return range === undefined ? '' : range.kind === 'preset' ? range.preset : `${range.start}/${range.end}`
}

/**
 * A list's time range: a preset window that ends now, or whole days typed or picked on a calendar in the dashboard's
 * display time zone, with today marked in that zone and moved at its midnight.
 */
export function DateRangeField({ label, value, onChange, presets, min, max, compact }: DateRangeFieldProps) {
  const { locale, timeZone } = useLocale()
  const today = useToday(timeZone)
  const chip = useRef<HTMLButtonElement>(null)
  // The days being typed: the picker commits on every digit, so while the end year is typed (2, 20, 202) the end
  // falls before the start. Such days stay here and are never sent, and a new value from outside replaces them.
  const [draft, setDraft] = useState(() => daysOf(value))
  const [shown, setShown] = useState(() => keyOf(value))

  if (shown !== keyOf(value)) {
    setShown(keyOf(value))
    setDraft(daysOf(value))
  }

  const earliest = min === undefined ? undefined : (parseCalendarDay(min) ?? undefined)
  const latest = max === undefined ? undefined : (parseCalendarDay(max) ?? undefined)
  // An earliest day after the latest leaves nothing to pick, and React Aria's calendar answers it with an endless render
  // that takes the page down, so the calendar shows every day disabled instead. The chip and the typed days stay
  // usable, so the operator can still read and clear the range.
  const isImpossible = earliest !== undefined && latest !== undefined && earliest.compare(latest) > 0
  const bounds = isImpossible ? {} : { minValue: earliest, maxValue: latest }
  const calendarBounds = isImpossible ? { minValue: earliest, maxValue: earliest, isDisabled: true } : {}

  function changeDays(next: DateRange | null) {
    setDraft(next)

    if (next === null) {
      onChange(undefined)
    } else if (next.end.compare(next.start) >= 0) {
      onChange({ kind: 'custom', start: next.start.toString(), end: next.end.toString() })
    }
  }

  const presetLabels = new Map(
    (presets ?? []).map((preset) => [
      preset.id,
      preset.label ??
        new Intl.NumberFormat(locale, { style: 'unit', unit: preset.unit, unitDisplay: 'short' }).format(preset.amount),
    ]),
  )
  const isPreset = value?.kind === 'preset'
  const showsDays = presets === undefined || !isPreset

  function choose(id: string) {
    if (id !== custom) {
      onChange({ kind: 'preset', preset: id })
    } else if (value?.kind !== 'custom') {
      // Custom starts from the days the preset covered, so the fields are never empty; with no range, from today.
      const day = calendarDayOf(Date.now(), timeZone)

      onChange(
        value ? toCustomTimeRange(value, { timeZone, presets: presets ?? [] }) : { kind: 'custom', start: day, end: day },
      )
    }
  }

  const presetRow =
    presets === undefined ? null : (
      <SegmentedControl
        label={`${label} presets`}
        options={[
          ...presets.map((preset) => ({ value: preset.id, label: presetLabels.get(preset.id) ?? preset.id })),
          { value: custom, label: 'Custom' },
        ]}
        value={isPreset ? value.preset : value ? custom : ''}
        onChange={choose}
      />
    )

  const calendar = (
    <RangeCalendar className="flex flex-col gap-2" {...calendarBounds}>
      <header className="flex items-center justify-between gap-2">
        <AriaButton slot="previous" className={`${navClass} size-7`}>
          <PreviousIcon className="text-current rtl:rotate-180" />
        </AriaButton>
        <Heading className="type-small-medium" />
        <AriaButton slot="next" className={`${navClass} size-7`}>
          <NextIcon className="text-current rtl:rotate-180" />
        </AriaButton>
      </header>
      <CalendarGrid className="border-separate border-spacing-y-0.5">
        {/* React Aria marks today in the browser's zone and only when it renders; this mark is today in the display
            zone, a dot under the day in the text's own colour so it reads on a plain, a ranged, and a picked day. */}
        {(date) => <CalendarCell date={date} className={cellClass} data-zone-today={isSameDay(date, today) || undefined} />}
      </CalendarGrid>
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
    const days = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', timeZone })
    const reading =
      value?.kind === 'preset'
        ? presetLabels.get(value.preset)
        : draft && value
          ? `${days.format(draft.start.toDate(timeZone))} – ${days.format(draft.end.toDate(timeZone))}`
          : undefined

    // The picker closes its popover on every range change by default; it stays open until Esc or a press outside,
    // so the operator sees the picked range and can clear it in the same place. The picker also anchors its popover
    // to its group, and moves the typing from the start day on to the end day inside that group. The chip is no group
    // and the popover clears the group context, so the chip anchors the popover (without it the popover never shows)
    // and the typed days take the picker's group, read outside the popover and given again inside it.
    return (
      <DateRangePicker
        className="inline-flex min-w-0"
        granularity="day"
        value={draft}
        onChange={changeDays}
        shouldCloseOnSelect={false}
        {...bounds}
      >
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
              <Dialog aria-label={label} className="flex flex-col gap-2.5 outline-none">
                {presetRow}
                {showsDays ? (
                  <>
                    {calendar}
                    {/* After the calendar in tab order, so its arrow keys stay where focus lands and `Tab` moves on to
                        typing. */}
                    <GroupContext value={group}>
                      <Group className={`${groupClass} pe-2`}>{inputs}</Group>
                    </GroupContext>
                  </>
                ) : null}
                {value ? (
                  <Button variant="ghost" onPress={() => changeDays(null)}>
                    {presets === undefined ? 'Clear days' : 'Clear range'}
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
    <DateRangePicker
      className="flex min-w-0 flex-col gap-1"
      granularity="day"
      value={draft}
      onChange={changeDays}
      {...bounds}
    >
      <Label className="hd-label">{label}</Label>
      {presetRow}
      {showsDays ? (
        <Group className={`${groupClass} pe-1`}>
          {inputs}
          <AriaButton className={`${navClass} ms-auto size-7`}>
            <CalendarIcon className="text-current" />
          </AriaButton>
        </Group>
      ) : null}
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
