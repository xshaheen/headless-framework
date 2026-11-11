// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'
import { FallIcon, NoChangeIcon, RiseIcon } from '../../icons/index.ts'
import { useFormat } from '../../locale/locale.tsx'

/** The direction of change that is good news: `up` for throughput, `down` for an error rate, `neutral` for neither. */
export type GoodDirection = 'up' | 'down' | 'neutral'

export interface TrendChipProps {
  /**
   * The served change as a fraction: `0.18` reads `+18%`. Pass only a change the server sends; the chip never derives
   * one, because only the server knows whether both periods it compared were complete.
   */
  value: number
  /** The direction that is good news; the colour follows it, not the sign. `up` by default. */
  goodDirection?: GoodDirection
  /** Text that replaces the percentage, for a change served in another unit: `−1.1 pt`, `+6 runs`. */
  children?: ReactNode
}

const arrows = { up: RiseIcon, down: FallIcon, flat: NoChangeIcon } as const
const roles = { good: 'ok', bad: 'fail', neutral: 'off' } as const satisfies Record<string, DashboardStateRole>

/** Whether a change is good, bad, or neither, given the direction that counts as good. No change is never either. */
export function trendSentiment(value: number, goodDirection: GoodDirection = 'up'): keyof typeof roles {
  if (value === 0 || goodDirection === 'neutral') {
    return 'neutral'
  }

  return (value > 0) === (goodDirection === 'up') ? 'good' : 'bad'
}

/**
 * A small pill after a figure with the served change: an arrow, then the signed amount, on a tint of the colour role
 * its meaning takes. The sign and the arrow carry the direction on their own, so a reader who cannot tell the colours
 * apart loses nothing; the text stays in the foreground colour, because most role colours fail 4.5:1 as text. The
 * arrow and the amount share one `<bdi>`, so in Arabic text the sign is not reordered away from its digits.
 */
export function TrendChip({ value, goodDirection = 'up', children }: TrendChipProps) {
  const format = useFormat()
  const Arrow = arrows[value > 0 ? 'up' : value < 0 ? 'down' : 'flat']

  return (
    <span
      className={`hd-tone-${roles[trendSentiment(value, goodDirection)]} inline-flex h-(--tag-height) flex-none items-center rounded-full bg-role/15 px-1.5 type-tag whitespace-nowrap text-foreground tabular-nums`}
    >
      <bdi className="inline-flex items-center gap-0.5 [&>svg]:size-3">
        <Arrow className="text-role" />
        {children ?? format.number(value, { style: 'percent', signDisplay: 'exceptZero', maximumFractionDigits: 1 })}
      </bdi>
    </span>
  )
}
