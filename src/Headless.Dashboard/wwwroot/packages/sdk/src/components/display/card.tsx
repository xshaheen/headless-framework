// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'
import type { IconComponent } from '../../icons/index.ts'
import { EmptyState } from './empty-state.tsx'

export interface CardProps {
  /** The card's heading, which also names its region: `Processing now`. */
  title: string
  /** A short served fact after the heading: `4 running`. */
  caption?: ReactNode
  /** One trailing control at the inline end: a link such as `All 9`, or the card's action. */
  action?: ReactNode
  /** What the card is about, from `@headless/dashboard-sdk/icons`, before the heading. */
  icon?: IconComponent
  /** What the card says, and how to fill it, when it holds no row. */
  empty?: ReactNode
  /** The icon beside `empty`. */
  emptyIcon?: IconComponent
  /** `CardRow`s; with none, the card shows `empty`. */
  children?: ReactNode
  /**
   * Whether the card takes two columns once `Cards` has room for two, for a card whose content reads better wide,
   * such as a chart of days. In one column it takes the one.
   */
  wide?: boolean
}

/** A dashboard card: a heading row with a caption and one trailing control, over rows or its empty state. */
export function Card({ title, caption, action, icon: Icon, empty, emptyIcon, children, wide = false }: CardProps) {
  const rows = Array.isArray(children) ? children.length > 0 : children != null && children !== false

  // The card fills its grid cell, so cards that share a row end at one line however many rows each holds; the rows
  // stay at the top and the card's surface takes the rest. Two columns of `Cards` need 2 × 22rem and the gap.
  return (
    <section
      className={`flex min-w-0 flex-col rounded-lg border border-border bg-card text-card-foreground${wide ? ' @min-[44.75rem]:col-span-2' : ''}`}
      aria-label={title}
    >
      {/* The heading row is prose and controls in sans; its caption is a served count, so it stays mono. */}
      <header className={`${row} type-nav [&>:not(h2,.hd-faint)]:ms-auto`}>
        <h2 className="flex items-center gap-2 type-nav-strong">
          {Icon ? <Icon /> : null}
          {title}
        </h2>
        {caption != null ? <span className="hd-faint type-mono tabular-nums">{caption}</span> : null}
        {action}
      </header>
      {rows ? <ul className="flex-1 [&>li:last-child]:border-0">{children}</ul> : <EmptyState icon={emptyIcon}>{empty}</EmptyState>}
    </section>
  )
}

const row = 'flex min-h-(--card-row-height) flex-wrap items-center gap-x-2.5 gap-y-1 border-b border-border py-1.5 ps-3.5 pe-3'

export interface CardRowProps {
  /** The state role whose dot leads the row. */
  tone?: DashboardStateRole
  /**
   * What the row is: a function, a cron job, an attention item; it ellipsizes. Without one (and without `tone`,
   * `meta`, `value`, and `actions`) the row is only its second line, such as a chart that fills the card.
   */
  name?: ReactNode
  /**
   * The name is a sentence rather than an identifier, such as an attention item the server could not be split into
   * a name and a count: it reads in the sans face; an identifier inside it still sets its own with `Mono`.
   */
  prose?: boolean
  /** Faint facts after the name: `node-a · 24 s`, a cron expression. */
  meta?: ReactNode
  /** The row's value at the inline end: `in 6 s`. */
  value?: ReactNode
  /** Row actions, such as Cancel. */
  actions?: ReactNode
  /**
   * A second line under the name, such as a progress bar and its caption. It is prose in sans; an identifier or a
   * number in it sets its own face.
   */
  children?: ReactNode
}

// The name keeps its letters while it fits in most of the line, so a fact beside it gives way first; only a name wider
// than that ellipsizes. A prose name sets its own face and weight, since the line's mono shorthand would cover it.
const monoName = 'min-w-0 max-w-[70%] shrink-0 overflow-hidden font-medium text-ellipsis'
const proseName = 'min-w-0 max-w-[70%] shrink-0 overflow-hidden text-ellipsis type-small-medium text-foreground'

/**
 * One row of a `Card`: dot, name, faint meta, value, and actions on one line, where only the name gives way, with an
 * optional second line.
 */
export function CardRow({ tone, name, prose = false, meta, value, actions, children }: CardRowProps) {
  const first = tone != null || name != null || meta != null || value != null || actions != null

  return (
    <li className={tone ? `hd-tone-${tone} ${row}` : row}>
      {/* The first line never wraps: only the name gives way, in an ellipsis, so a value or Cancel stays beside it. Its
          name, meta, and value are identifiers, counts, and times, so the line is mono; a control keeps its own sans. */}
      {first ? (
        <div className="flex min-w-0 basis-full items-center gap-2.5 type-mono whitespace-nowrap [&>b+*]:ms-auto">
          {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
          <b className={prose ? proseName : monoName}>{name}</b>
          {meta != null ? <span className="hd-faint min-w-0 overflow-hidden text-ellipsis">{meta}</span> : null}
          {value}
          {actions}
        </div>
      ) : null}
      {/* `min-w-0` lets a wide table inside scroll in its own box instead of widening the card. */}
      {children != null ? (
        <div className="flex min-w-0 basis-full flex-col gap-1 type-small text-muted-foreground">{children}</div>
      ) : null}
    </li>
  )
}

/**
 * The dashboard's cards in columns that wrap to one at narrow widths. Cards in one row share its height. The grid is
 * the query container a `wide` card measures, so it spans two columns only when two fit.
 */
export function Cards({ children }: { children: ReactNode }) {
  return (
    <div className="@container grid grid-cols-[repeat(auto-fit,minmax(min(100%,22rem),1fr))] gap-3 px-4 py-3.5">
      {children}
    </div>
  )
}
