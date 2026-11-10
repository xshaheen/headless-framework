// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'

export interface PageHeaderProps {
  /** The page's name: `Failed`, `Recurring jobs`. */
  title: string
  /** The state role whose dot leads the title, on a page that lists one state. */
  tone?: DashboardStateRole
  /** How many rows the page holds, as served: `23`, `3 · 2 running`, `500+`. */
  count?: ReactNode
  /** One sentence on what the page holds or what comes next. */
  description?: ReactNode
  /** Anything else on the title row after the description; filter chips go in the toolbar's `chips`, so the actions keep the title's line. */
  children?: ReactNode
  /** The selection cluster and the page's primary action, at the title row's inline end. */
  actions?: ReactNode
  /** The row of filters under the title: a `FilterToolbar`, or a node switch. */
  toolbar?: ReactNode
}

/**
 * The rows above a list. The title row holds the title and count, the description, and the actions at its end, so
 * the primary action lines up with the title in every view; the toolbar row under it holds the chips, the search, and
 * the filters, so they never push the action down or wrap between the title and the list.
 */
export function PageHeader({ title, tone, count, description, children, actions, toolbar }: PageHeaderProps) {
  return (
    <header className="flex flex-none flex-col gap-2 border-b border-border px-4 py-2.5">
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <h1 className={`flex items-center gap-2 type-title ${tone ? `hd-tone-${tone}` : ''}`}>
          {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
          {title}
        </h1>
        {count != null ? <span className="type-mono text-faint tabular-nums">{count}</span> : null}
        {description ? <p className="hd-hint">{description}</p> : null}
        {children}
        {actions ? <div className="ms-auto flex flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      {toolbar}
    </header>
  )
}
