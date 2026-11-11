// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { Meter as AriaMeter } from 'react-aria-components'
import type { DashboardStateRole } from '../../define-module.ts'

/** From `from` upward, in the meter's own units, the fill takes `tone`. */
export interface MeterThreshold {
  from: number
  tone: DashboardStateRole
}

export interface MeterProps {
  /** What the meter measures, which also names it for assistive technology: `Queue depth`. */
  label: string
  /** The served quantity. */
  value: number
  /** The bottom of the range; `0` by default. */
  min?: number
  /** The top of the range, such as the queue's capacity. */
  max: number
  /**
   * The value as text, saying what a crossed threshold means: `9,400 of 10,000 messages, near the limit`. It is shown
   * and read out, so the tone only reinforces it.
   */
  valueLabel: string
  /** The fill's tone below every threshold; `proc` by default. */
  tone?: DashboardStateRole
  /**
   * Where the fill changes tone: `[{ from: 8000, tone: 'sched' }, { from: 10000, tone: 'fail' }]`. Each one inside the
   * range also draws a mark on the track.
   */
  thresholds?: readonly MeterThreshold[]
}

/** The tone of the highest threshold the value has reached, or the meter's own tone below them all. */
export function meterTone(value: number, tone: DashboardStateRole, thresholds: readonly MeterThreshold[] = []) {
  let reached: MeterThreshold | undefined

  for (const threshold of thresholds) {
    if (value >= threshold.from && (!reached || threshold.from >= reached.from)) {
      reached = threshold
    }
  }

  return reached?.tone ?? tone
}

/**
 * Where a quantity stands in a known range, such as a queue's depth against its capacity; work that advances is a
 * `ProgressBar`. The track and its text match the progress bar's. Thresholds tint the fill and mark the track.
 */
export function Meter({ label, value, min = 0, max, valueLabel, tone = 'proc', thresholds = [] }: MeterProps) {
  const range = max - min
  const share = (at: number) => (range > 0 ? Math.min(100, Math.max(0, ((at - min) / range) * 100)) : 0)
  const marks = range > 0 ? thresholds.filter((threshold) => threshold.from > min && threshold.from < max) : []

  return (
    <AriaMeter
      className="flex flex-col gap-1"
      aria-label={label}
      value={value}
      minValue={min}
      maxValue={range > 0 ? max : min + 1}
      valueLabel={valueLabel}
    >
      <span className="order-1 type-small text-faint tabular-nums">{valueLabel}</span>
      <span className="relative block h-1 max-w-(--bar-max) overflow-hidden rounded-xs bg-muted" aria-hidden="true">
        <i
          className={`hd-tone-${meterTone(value, tone, thresholds)} block h-full rounded-xs bg-role`}
          style={{ inlineSize: `${share(value)}%` }}
        />
        {/* Logical insets, so a right-to-left page measures each mark from its own start edge. */}
        {marks.map((mark) => (
          <i
            key={mark.from}
            data-threshold={mark.from}
            className="absolute inset-y-0 w-px bg-foreground/35"
            style={{ insetInlineStart: `${share(mark.from)}%` }}
          />
        ))}
      </span>
    </AriaMeter>
  )
}
