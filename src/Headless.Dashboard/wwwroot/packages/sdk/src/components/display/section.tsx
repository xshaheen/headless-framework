'use no memo'

import type { ReactNode } from 'react'

export interface SectionProps {
  /** The heading, which also names the region: `Circuit breakers`. */
  label: string
  /** A short served fact after the heading, such as a count. */
  caption?: ReactNode
  /** One trailing control at the inline end, such as a link to the full list. */
  action?: ReactNode
  children: ReactNode
}

/**
 * A second block on a page under its main list, such as the circuit breakers under the consumers: a hairline, the
 * heading row on the page gutter, then its content. A detail panel's inner headings use `Fields` and `Text` instead.
 */
export function Section({ label, caption, action, children }: SectionProps) {
  return (
    <section aria-label={label} className="flex flex-col border-t border-border pt-3.5">
      <div className="flex flex-wrap items-center gap-2.5 px-4 pb-1">
        <h2 className="type-title">{label}</h2>
        {caption != null ? <span className="type-mono text-faint tabular-nums">{caption}</span> : null}
        {action != null ? <span className="ms-auto">{action}</span> : null}
      </div>
      {children}
    </section>
  )
}
