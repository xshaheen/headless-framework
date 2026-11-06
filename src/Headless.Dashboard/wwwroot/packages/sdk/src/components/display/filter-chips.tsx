// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'

export interface FilterChipProps {
  /** What the chip filters by: `TimeoutException`, `correlation`. */
  label: string
  /** The value the filter holds, shown mono: an id or a fingerprint. */
  value?: ReactNode
  /** How many rows the chip matches. */
  count?: ReactNode
  /** The count is of failures, so it reads in the failure colour. */
  failing?: boolean
  /** The full name, when the label shortens it. */
  title?: string
  /** A toggle chip: whether its filter is on, and the press that flips it. */
  pressed?: boolean
  onPress?: () => void
  /** A filter that is on and can only be cleared, such as one a link set; the chip gets a `Clear` button instead. */
  onClear?: () => void
}

/** One filter as a chip: a toggle with its count, or a set filter with `Clear`. */
export function FilterChip({ label, value, count, failing, title, pressed, onPress, onClear }: FilterChipProps) {
  const body = (
    <>
      {label}
      {value != null ? <b className="hd-mono">{value}</b> : null}
      {count != null ? <b className={failing ? 'hd-fail' : undefined}>{count}</b> : null}
    </>
  )

  return onClear ? (
    <span className="hd-fchip" title={title}>
      {body}
      <button type="button" aria-label={`Clear ${label} filter`} onClick={onClear}>
        ×
      </button>
    </span>
  ) : (
    <button type="button" className="hd-fchip" title={title} aria-pressed={pressed} onClick={onPress}>
      {body}
    </button>
  )
}

/** A group of filter chips, named for the filter: `Filter by reason`. The `All N` chip leads it. */
export function FilterChips({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div role="group" aria-label={label} className="hd-chips">
      {children}
    </div>
  )
}
