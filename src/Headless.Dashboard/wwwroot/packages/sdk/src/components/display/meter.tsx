// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { Meter as AriaMeter } from 'react-aria-components'
import type { DashboardStateRole } from '../../define-module.ts'

/** A point on the meter's scale: at `from` and above, the fill switches to `tone`. */
export interface MeterThreshold {
  from: number
  tone: DashboardStateRole
}

export interface MeterProps {
  /** What is measured; it also names the meter for assistive technology: `Queue depth`. */
  label: string
  /** The served quantity. */
  value: number
  /** Where the scale starts; `0` by default. */
  min?: number
  /** Where the scale ends, such as the queue's capacity. */
  max: number
  /**
   * The reading in words, shown under the label and announced in place of a bare percentage. Say what the colour
   * means, since colour alone does not: `4,812 of 5,000 messages, over the warning level`.
   */
  valueLabel: string
  /** The colour role the fill takes until it crosses a threshold; `proc` by default. */
  tone?: DashboardStateRole
  /**
   * Points on the scale where the fill changes colour, such as `[{ from: 4000, tone: 'sched' }, { from: 5000, tone:
   * 'fail' }]`. A point strictly between `min` and `max` is also drawn as a tick on the track.
   */
  thresholds?: readonly MeterThreshold[]
}

/** The colour role for `value`: the last threshold it has passed, counting up the scale, or `tone` if none. */
export function meterTone(value: number, tone: DashboardStateRole, thresholds: readonly MeterThreshold[] = []) {
  return [...thresholds]
    .sort((a, b) => a.from - b.from)
    .reduce<DashboardStateRole>((current, threshold) => (value >= threshold.from ? threshold.tone : current), tone)
}

/**
 * A served quantity against a fixed scale, such as a queue's depth against its capacity. Work in flight that will
 * finish is a `ProgressBar` instead; the two share a track and a text line. Thresholds recolour the fill and tick the
 * track.
 */
export function Meter({ label, value, min = 0, max, valueLabel, tone = 'proc', thresholds = [] }: MeterProps) {
  const span = max - min
  const percent = (at: number) => (span > 0 ? Math.min(100, Math.max(0, ((at - min) / span) * 100)) : 0)
  const ticks = span > 0 ? thresholds.filter(({ from }) => from > min && from < max) : []

  return (
    <AriaMeter
      className="flex flex-col gap-1"
      aria-label={label}
      value={value}
      minValue={min}
      maxValue={span > 0 ? max : min + 1}
      valueLabel={valueLabel}
    >
      <span className="order-1 type-small text-faint tabular-nums">{valueLabel}</span>
      <span className="relative block h-1 max-w-(--bar-max) overflow-hidden rounded-xs bg-muted" aria-hidden="true">
        <i
          className={`hd-tone-${meterTone(value, tone, thresholds)} block h-full rounded-xs bg-role`}
          style={{ inlineSize: `${percent(value)}%` }}
        />
        {/* Placed with a logical inset, so on a right-to-left page a tick counts from the right edge like the fill. */}
        {ticks.map(({ from }) => (
          <i
            key={from}
            data-threshold={from}
            className="absolute top-0 h-full w-px bg-edge"
            style={{ insetInlineStart: `${percent(from)}%` }}
          />
        ))}
      </span>
    </AriaMeter>
  )
}
