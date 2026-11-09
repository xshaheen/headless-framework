// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import { Link, type To } from 'react-router'
import type { DashboardStateRole } from '../../define-module.ts'

export interface StatCardProps {
  /** What the value counts: `Failed`. */
  label: string
  /** The value as served: `23`, `500+`, `98.2 %`. */
  value: ReactNode
  /** The state role whose dot leads the label. */
  tone?: DashboardStateRole
  /** A line the server serves with the value, such as `all tenants` or `−1.1 pt vs previous 24 h`; nothing otherwise. */
  caption?: ReactNode
  /** The list the value counts, filtered to it; the whole card links there. */
  to?: To
}

const stat = 'flex flex-col gap-0.5 border-e border-border px-4 pt-2.5 pb-1.5 text-foreground no-underline'

/**
 * One served value with its label and state dot. It draws no sparkline and no empty chart frame: a trend appears
 * only once the server serves its series.
 */
export function StatCard({ label, value, tone, caption, to }: StatCardProps) {
  const body = (
    <>
      <span className={`flex items-center gap-2 type-small text-muted-foreground ${tone ? `hd-tone-${tone}` : ''}`}>
        {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
        {label}
      </span>
      {/* The spaces keep the link's name `Failed 23`, not `Failed23`; the flex column drops them from the layout. */}{' '}
      <span className="type-stat tabular-nums">{value}</span>
      {caption ? <> <span className="hd-label">{caption}</span></> : null}
    </>
  )

  return to ? (
    <Link to={to} className={`${stat} transition-fast hover:bg-accent`}>
      {body}
    </Link>
  ) : (
    <div className={stat}>{body}</div>
  )
}

/** The row of stat cards above a page, named for what it summarizes: `Runs by state`. Up to six cards. */
export function StatStrip({ label, children }: { label: string; children: ReactNode }) {
  return (
    <section
      aria-label={label}
      className="grid auto-cols-[minmax(0,1fr)] grid-flow-col border-b border-border bg-card max-md:grid-flow-row max-md:grid-cols-2"
    >
      {children}
    </section>
  )
}
