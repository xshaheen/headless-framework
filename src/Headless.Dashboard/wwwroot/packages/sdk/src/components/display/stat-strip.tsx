// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import { Link, type To } from 'react-router'
import type { DashboardStateRole } from '../../define-module.ts'
import type { IconComponent } from '../../icons/index.ts'

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
  /** What the value is about, from `@headless/dashboard-sdk/icons`, at the label's inline end; never its state. */
  icon?: IconComponent
}

const stat = 'flex min-h-(--statstrip-height) flex-col gap-0.5 border-e border-border px-4 pt-2.5 pb-2 text-foreground no-underline'

/**
 * One served value with its label and state dot. It draws no sparkline and no empty chart frame: a trend appears
 * only once the server serves its series.
 */
export function StatCard({ label, value, tone, caption, to, icon: Icon }: StatCardProps) {
  const body = (
    <>
      <span className={`flex items-center gap-2 type-small text-muted-foreground ${tone ? `hd-tone-${tone}` : ''}`}>
        {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
        <span className="truncate" title={label}>
          {label}
        </span>
        {Icon ? <Icon className="ms-auto" /> : null}
      </span>
      {/* The spaces keep the link's name `Failed 23`, not `Failed23`; the flex column drops them from the layout. */}{' '}
      <span className="type-stat tabular-nums">{value}</span>
      {/* A served sentence can run past the cell: it keeps to two lines and shows whole on hover, so the strip stays
          one height at every width. */}
      {caption ? (
        <>
          {' '}
          <span className="hd-label line-clamp-2 text-pretty" title={typeof caption === 'string' ? caption : undefined}>
            {caption}
          </span>
        </>
      ) : null}
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
    // Six cells share the width only where each keeps about 200 px; below 1280 px they wrap three to a row, and on a
    // phone two, so a label never breaks into a third line and the strip reads as one block.
    <section
      aria-label={label}
      className="grid auto-cols-[minmax(0,1fr)] grid-flow-col border-b border-border bg-card max-xl:grid-flow-row max-xl:grid-cols-3 max-md:grid-cols-2 max-xl:[&>*:nth-child(-n+3)]:border-b max-md:[&>*:nth-child(3)]:border-b-0 max-md:[&>*:nth-child(-n+2)]:border-b"
    >
      {children}
    </section>
  )
}
