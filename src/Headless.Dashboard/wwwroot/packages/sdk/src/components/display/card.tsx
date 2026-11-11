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

  // Cards that share a row and hold rows end at one line, however many rows each holds: the rows stay at the top and
  // the surface takes the rest. A card with no row is only its heading and its empty state, so it never stretches
  // into a tall box around one sentence. Two columns of `Cards` need 2 × 22rem and the gap.
  return (
    <section
      className={`flex min-w-0 flex-col rounded-lg border border-border bg-card text-card-foreground${rows ? '' : ' self-start'}${wide ? ' @min-[44.75rem]:col-span-2' : ''}`}
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
   * What the row is: a function, a cron job, an attention item. Without one (and without `tone`, `meta`, `value`, and
   * `actions`) the row is only its second line, such as a chart that fills the card.
   */
  name?: ReactNode
  /**
   * The name is a sentence rather than an identifier, such as an attention item the server could not be split into
   * a name and a count: it reads in the sans face; an identifier inside it still sets its own with `Mono`.
   */
  prose?: boolean
  /** Faint facts after the name: `node-a · 24 s`, a cron expression, `37 failed runs`. */
  meta?: ReactNode
  /** The row's value at the inline end, in a column every row shares: `in 6 s`. */
  value?: ReactNode
  /** Row actions, such as Cancel. */
  actions?: ReactNode
  /**
   * The row's first line as plain text, `billing.charge 37 failed runs`, shown on hover of the name when the name or
   * its facts had to shorten. Leave it out when the name can never be wider than the card.
   */
  title?: string
  /**
   * A second line under the name, such as a progress bar and its caption. It is prose in sans; an identifier or a
   * number in it sets its own face.
   */
  children?: ReactNode
}

// A prose name sets its own face and weight, since the line's mono shorthand would otherwise cover it.
const monoName = 'max-w-full truncate font-medium'
const proseName = 'max-w-full truncate type-small-medium text-foreground'

/**
 * One row of a `Card`: dot, name and faint meta, value, and actions, with an optional second line. The name keeps
 * its letters: it is sized to its content, and the facts beside it move under it when the card is too narrow for
 * both on one line. Only a name or a fact wider than the card itself ends in an ellipsis, with the full text in
 * `title`. The value column is one width on every row, so the times line up down the card.
 */
export function CardRow({ tone, name, prose = false, meta, value, actions, title, children }: CardRowProps) {
  const first = tone != null || name != null || meta != null || value != null || actions != null
  // A row that leads somewhere lights up under the pointer and around keyboard focus, as a table row does; a row
  // with only a value or a button gives no such cue, since there is nothing to open.
  const interactive = 'transition-fast has-[a]:hover:bg-accent has-[a:focus-visible]:bg-accent'

  return (
    <li className={`${tone ? `hd-tone-${tone} ` : ''}${row} ${interactive}`}>
      {/* The first line is a grid: the dot where there is one, the name cell that takes the width, then the value
          and the actions at their own widths. A missing part leaves no column behind (a hidden grid item would shift
          the rest into its column). Names, counts, and times are mono; a control inside keeps its own sans. */}
      {first ? (
        <div
          className={`grid min-w-0 basis-full items-center gap-x-2.5 type-mono ${tone ? 'grid-cols-[auto_minmax(0,1fr)_auto_auto]' : 'grid-cols-[minmax(0,1fr)_auto_auto]'}`}
        >
          {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
          {/* The name and its facts wrap as a row: while both fit they share the line, and a fact that does not fit
              drops under the name with the whole width to itself. */}
          <div className="flex min-w-0 flex-wrap items-baseline gap-x-2.5 gap-y-0" title={title}>
            {name != null ? <b className={prose ? proseName : monoName}>{name}</b> : null}
            {meta != null ? <span className="hd-faint max-w-full truncate">{meta}</span> : null}
          </div>
          {value != null ? (
            <span className="min-w-(--card-value-width) text-end whitespace-nowrap tabular-nums">{value}</span>
          ) : null}
          {actions != null ? <span className="flex items-center gap-1">{actions}</span> : null}
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
 * The dashboard's cards in columns that wrap to one at narrow widths. Cards in one row share its height while they
 * hold rows; an empty card stays as tall as its empty state. The grid is the query container a `wide` card measures,
 * so it spans two columns only when two fit.
 */
export function Cards({ children }: { children: ReactNode }) {
  return (
    <div className="@container grid grid-cols-[repeat(auto-fit,minmax(min(100%,22rem),1fr))] gap-3 px-4 py-3.5">
      {children}
    </div>
  )
}
