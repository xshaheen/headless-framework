// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useState } from 'react'
import { Button as AriaButton, Tooltip as AriaTooltip, TooltipTrigger } from 'react-aria-components'
import { useFormat } from '../../locale/locale.tsx'
import { relativeClock, useRelativeTime } from '../../locale/relative-time.ts'
import { tooltipSurface } from '../tooltip.tsx'
import { Text } from './text.tsx'

export interface TimestampProps {
  /** The ISO time the server stored; nothing shows `missing`. */
  value: string | null | undefined
  /** The IANA zone the exact time reads in, such as the scheduler's for a cron field; the display zone otherwise. */
  zone?: string
  /**
   * A time something is due at, such as a next fire: once it passes it reads `due 42 sec. ago`, because the work has
   * not run yet, where a bare `42 sec. ago` would say it did.
   */
  scheduled?: boolean
  /** What shows in place of a time the server has not stored yet. */
  missing?: string
  /**
   * Leaves the trigger out of the tab order, for a widget that keeps one tab stop and moves focus itself, such as the
   * `Timeline` grid: a tab stop per time would break its arrow-key model. Hover and press still show the exact time.
   */
  excludeFromTabOrder?: boolean
}

/**
 * A timestamp as relative time in the short style, `4 min. ago`, counted in whole units toward zero, with the exact
 * time, zone, and offset in a tooltip. Every time on the dashboard reads this way, so one page never mixes
 * `in 2 hours` with `in 7 hr.`. The text moves on as time passes without new data, from one timer the whole page
 * shares that sleeps until the next time whose text changes.
 *
 * It is a button, because hover has no keyboard or touch equivalent: focus shows the exact time, and so does a press.
 * Like every React Aria press it stays its own, so in a table row it never opens the row.
 */
export function Timestamp({ value, missing = 'Not yet', ...shown }: TimestampProps) {
  return value ? <Shown value={value} {...shown} /> : <Text tone="hint">{missing}</Text>
}

function Shown({ value, zone, scheduled = false, excludeFromTabOrder }: Omit<TimestampProps, 'missing'> & { value: string }) {
  const format = useFormat()
  const relative = useRelativeTime(value)
  const [isOpen, setOpen] = useState(false)
  const at = Date.parse(value)

  // A value that is not a time shows as it came: an empty cell would hide the fault.
  if (Number.isNaN(at)) {
    return <span>{value}</span>
  }

  return (
    <TooltipTrigger isOpen={isOpen} onOpenChange={setOpen} delay={400} closeDelay={0} shouldCloseOnPress={false}>
      <AriaButton
        className="cursor-default rounded-sm whitespace-nowrap outline-none data-focus-visible:outline-solid data-focus-visible:outline-2 data-focus-visible:outline-offset-2 data-focus-visible:outline-ring"
        excludeFromTabOrder={excludeFromTabOrder}
        onPress={() => setOpen(true)}
      >
        <time dateTime={value}>{scheduled && at <= relativeClock.now() ? `due ${relative}` : relative}</time>
      </AriaButton>
      <AriaTooltip placement="bottom" offset={4} containerPadding={8} className={tooltipSurface}>
        {zone ? format.zonedTime(value, zone) : format.exactTime(value)}
      </AriaTooltip>
    </TooltipTrigger>
  )
}
