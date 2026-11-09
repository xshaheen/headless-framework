// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'

export interface FieldsProps {
  /** The section's accessible name; usually the text of the heading inside it. */
  label: string
  children: ReactNode
}

/** A named section of stacked facts on a detail page, such as `Where it runs` or `Next fires`. */
export function Fields({ label, children }: FieldsProps) {
  return (
    <section aria-label={label} className="hd-fields">
      {children}
    </section>
  )
}

/** A row of buttons that wraps on a narrow screen. */
export function Actions({ children }: { children: ReactNode }) {
  return <span className="hd-actions">{children}</span>
}
