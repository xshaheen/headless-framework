// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import { CloseIcon } from '../../icons/index.ts'

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

const chip = 'inline-flex h-(--fchip-height) items-center gap-1.5 rounded-md border px-2 type-small text-muted-foreground transition-fast'

/** One filter as a chip: a toggle with its count, or a set filter with `Clear`. */
export function FilterChip({ label, value, count, failing, title, pressed, onPress, onClear }: FilterChipProps) {
  const body = (
    <>
      {label}
      {value != null ? <b className="hd-mono">{value}</b> : null}
      {count != null ? <b className={failing ? 'type-label-mono text-destructive' : 'type-label-mono'}>{count}</b> : null}
    </>
  )

  return onClear ? (
    <span className={`${chip} border-primary bg-primary-soft`} title={title}>
      {body}
      <button type="button" className="cursor-pointer text-inherit" aria-label={`Clear ${label} filter`} onClick={onClear}>
        <CloseIcon className="text-current" />
      </button>
    </span>
  ) : (
    <button type="button" className={`${chip} cursor-pointer border-border hover:border-edge hover:text-foreground aria-pressed:border-primary aria-pressed:bg-primary-soft aria-pressed:text-foreground`} title={title} aria-pressed={pressed} onClick={onPress}>
      {body}
    </button>
  )
}

/** A group of filter chips, named for the filter: `Filter by reason`. The `All N` chip leads it. */
export function FilterChips({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div role="group" aria-label={label} className="flex flex-wrap items-center gap-1.5">
      {children}
    </div>
  )
}
