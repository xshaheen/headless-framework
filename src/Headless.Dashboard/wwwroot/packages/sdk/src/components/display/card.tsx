// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'

export interface CardProps {
  /** The card's heading, which also names its region: `Processing now`. */
  title: string
  /** A short served fact after the heading: `4 running`. */
  caption?: ReactNode
  /** One trailing control at the inline end: a link such as `All 9`, or the card's action. */
  action?: ReactNode
  /** What the card says, and how to fill it, when it holds no row. */
  empty?: ReactNode
  /** `CardRow`s; with none, the card shows `empty`. */
  children?: ReactNode
}

/** A dashboard card: a heading row with a caption and one trailing control, over rows or its empty state. */
export function Card({ title, caption, action, empty, children }: CardProps) {
  const rows = Array.isArray(children) ? children.length > 0 : children != null && children !== false

  return (
    <section className="min-w-0 rounded-lg border border-border bg-card text-card-foreground" aria-label={title}>
      <header className={`${row} [&>:not(h2,.hd-faint)]:ms-auto`}>
        <h2 className="type-nav font-semibold">{title}</h2>
        {caption != null ? <span className="hd-faint">{caption}</span> : null}
        {action}
      </header>
      {rows ? <ul className="[&>li:last-child]:border-0">{children}</ul> : <p className="hd-hint p-3.5">{empty}</p>}
    </section>
  )
}

// A heading or link row of a card: one hairline under each, the mono face, the dot inset from the edge.
const row = 'flex min-h-(--card-row-height) flex-wrap items-center gap-x-2.5 gap-y-1 border-b border-border py-1.5 ps-3.5 pe-3 type-mono'

export interface CardRowProps {
  /** The state role whose dot leads the row. */
  tone?: DashboardStateRole
  /** What the row is: a function, a cron job, an attention item; it ellipsizes. */
  name: ReactNode
  /** Faint facts after the name: `node-a · 24 s`, a cron expression. */
  meta?: ReactNode
  /** The row's value at the inline end: `in 6 s`. */
  value?: ReactNode
  /** Row actions, such as Cancel. */
  actions?: ReactNode
  /** A second line under the name, such as a progress bar and its caption. */
  children?: ReactNode
}

/**
 * One row of a `Card`: dot, name, faint meta, value, and actions on one line, where only the name gives way, with an
 * optional second line.
 */
export function CardRow({ tone, name, meta, value, actions, children }: CardRowProps) {
  return (
    <li className={tone ? `hd-tone-${tone} ${row}` : row}>
      {/* The first line never wraps: only the name gives way, in an ellipsis, so a value or Cancel stays beside it. */}
      <div className="flex min-w-0 basis-full items-center gap-2.5 whitespace-nowrap [&>b+*]:ms-auto">
        {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
        <b className="min-w-0 overflow-hidden font-medium text-ellipsis">{name}</b>
        {meta != null ? <span className="hd-faint">{meta}</span> : null}
        {value}
        {actions}
      </div>
      {children != null ? <div className="basis-full">{children}</div> : null}
    </li>
  )
}

/** The dashboard's cards in columns that wrap to one at narrow widths. */
export function Cards({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,22rem),1fr))] items-start gap-3 px-4 py-3.5">{children}</div>
}
