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
  /** Filter chips or a filter field, after the description. */
  children?: ReactNode
  /** The selection cluster and the page's primary action, at the inline end. */
  actions?: ReactNode
}

/** The row above a list: title and count, a description, the filters, and the actions at the end. */
export function PageHeader({ title, tone, count, description, children, actions }: PageHeaderProps) {
  return (
    <header className="hd-page-header">
      <h1 className={tone ? `hd-tone-${tone}` : undefined}>
        {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
        {title}
      </h1>
      {count != null ? <span className="hd-page-count">{count}</span> : null}
      {description ? <p className="hd-hint">{description}</p> : null}
      {children}
      {actions ? <div className="hd-page-actions">{actions}</div> : null}
    </header>
  )
}
