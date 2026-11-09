// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'

/** `pill` for a standing state (`Paused`), `small` for a note on a row (`late 4 s`), `filled` for a kind, `square` for a name. */
export type TagVariant = 'pill' | 'small' | 'filled' | 'square'

export interface TagProps {
  children: ReactNode
  variant?: TagVariant
  /** The state role whose dot leads the tag; the text keeps its tone, because most roles fail 4.5:1 as text. */
  tone?: DashboardStateRole
  /** The full text, when the tag shortens it. */
  title?: string
}

// shadcn's badge at the dashboard's two tag heights: outline pills for states, filled and square tags for kinds and names.
const base = 'inline-flex flex-none items-center gap-1 border px-1.5 type-tag whitespace-nowrap text-muted-foreground'
const variants: Record<TagVariant, string> = {
  small: `${base} h-(--tag-height) rounded-full border-border`,
  pill: `${base} h-(--tag-pill-height) rounded-full border-edge`,
  filled: `${base} h-(--tag-pill-height) rounded-sm border-raised bg-muted`,
  square: `${base} h-(--tag-pill-height) rounded-sm border-border`,
}

/** A short fact on an object or a row, such as `Paused`, `cron`, or why a bulk action refused the row. */
export function Tag({ children, variant = 'small', tone, title }: TagProps) {
  return (
    <span className={`hd-tag ${variants[variant]}${tone ? ` hd-tone-${tone}` : ''}`} title={title}>
      {tone ? <i className="hd-mark" aria-hidden="true" /> : null}
      {children}
    </span>
  )
}
