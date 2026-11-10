// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useFormat } from '../../locale/locale.tsx'
import { relativeTickOf, useNow } from '../../locale/now.ts'
import { Text } from './text.tsx'

export interface RelativeTimeProps {
  /** The ISO time the server stored; nothing shows `missing`. */
  value: string | null | undefined
  /** The IANA zone the hover title reads the exact time in; the display zone otherwise. */
  zone?: string
  /**
   * A time something is due at, such as a next fire: once it passes it reads `due 42 sec. ago`, because the work has
   * not run yet, where a bare `42 sec. ago` would say it did.
   */
  scheduled?: boolean
  /** What shows in place of a time the server has not stored yet. */
  missing?: string
}

/**
 * A timestamp as relative time in the short style, `4 min. ago`, with the exact time, zone, and offset on hover. Every
 * time on the dashboard reads this way, so one page never mixes `in 2 hours` with `in 7 hr.`, and a list column stays
 * narrow enough for a phone. The text moves on as time passes without new data: a next fire reads `in 3 sec.`, then
 * `due 2 sec. ago`, on a timer shared by every time with the same distance.
 */
export function RelativeTime({ value, zone, scheduled = false, missing = 'Not yet' }: RelativeTimeProps) {
  const format = useFormat()
  const at = value ? Date.parse(value) : Number.NaN
  const now = useNow(Number.isNaN(at) ? null : relativeTickOf(at))

  if (!value) {
    return <Text tone="hint">{missing}</Text>
  }

  const relative = format.relativeTime(value, { style: 'short', now })

  return (
    <time dateTime={value} title={zone ? format.zonedTime(value, zone) : format.exactTime(value)}>
      {scheduled && at <= now ? `due ${relative}` : relative}
    </time>
  )
}
