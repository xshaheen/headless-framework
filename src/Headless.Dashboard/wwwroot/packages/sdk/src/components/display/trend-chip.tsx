// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'
import { FallIcon, NoChangeIcon, RiseIcon } from '../../icons/index.ts'
import { useFormat } from '../../locale/locale.tsx'

/** Which way a change is good news: `up` for a success rate, `down` for a failure rate, `neutral` when neither is. */
export type TrendGoodDirection = 'up' | 'down' | 'neutral'

export interface TrendChipProps {
  /**
   * The change the server served, as a ratio: `0.18` for +18 %. The SDK never computes one from two figures, because
   * only the server knows whether the periods it compared are complete.
   */
  value: number
  /** Which way is good news; it sets the tone, so a falling failure rate reads as good. `up` by default. */
  goodDirection?: TrendGoodDirection
  /** The change in another unit, such as `−1.1 pt` or `+6`; the arrow and the tone still follow `value`. */
  children?: ReactNode
}

const icons = { up: RiseIcon, down: FallIcon, flat: NoChangeIcon } as const

// Good reads in the success role, bad in the failure role, and anything that is neither in the off role.
const tones = { good: 'ok', bad: 'fail', neutral: 'off' } as const satisfies Record<string, DashboardStateRole>

/** The tone of a change: whether it moved the way that is good news. A change of zero is neither. */
export function trendSentiment(value: number, goodDirection: TrendGoodDirection = 'up') {
  const direction = value > 0 ? 'up' : value < 0 ? 'down' : 'flat'

  return direction === 'flat' || goodDirection === 'neutral' ? 'neutral' : direction === goodDirection ? 'good' : 'bad'
}

/**
 * A served change beside a figure: an arrow and the signed change on a soft fill in the tone of its meaning. The sign
 * and the arrow say the direction, so the tone is never the only cue; the text keeps the foreground tone, because most
 * roles are under 4.5:1 as text. Both sit in one `<bdi>`, so in a right-to-left line the sign stays with its number.
 */
export function TrendChip({ value, goodDirection = 'up', children }: TrendChipProps) {
  const format = useFormat()
  const sentiment = trendSentiment(value, goodDirection)
  const Icon = icons[value > 0 ? 'up' : value < 0 ? 'down' : 'flat']

  return (
    <span
      className={`hd-tone-${tones[sentiment]} inline-flex h-(--tag-height) flex-none items-center rounded-full bg-role/15 px-1.5 type-tag whitespace-nowrap text-foreground tabular-nums`}
      data-sentiment={sentiment}
    >
      <bdi className="inline-flex items-center gap-0.5 [&>svg]:size-3">
        <Icon className="text-role" />
        {children ?? format.number(value, { style: 'percent', signDisplay: 'exceptZero', maximumFractionDigits: 1 })}
      </bdi>
    </span>
  )
}
