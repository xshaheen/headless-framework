// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'

/**
 * `hint` for secondary text, `faint` for a quiet aside, `label` for a small heading over a group, `title` for a
 * section heading, `error` for a failure, `footnote` for a mono note under a grid.
 */
export type TextTone = 'hint' | 'faint' | 'label' | 'title' | 'error' | 'footnote'

export interface TextProps {
  children: ReactNode
  tone: TextTone
  /** The element, so a heading stays a heading and a paragraph a paragraph; `span` by default. */
  as?: 'span' | 'p' | 'h2' | 'h3'
  /** `alert` for an error the operator must hear, `status` for a polite note. */
  role?: 'alert' | 'status'
}

/** A run of text in one of the dashboard's text tones, so a module never names a style. */
export function Text({ children, tone, as: Element = 'span', role }: TextProps) {
  return (
    <Element role={role} className={`hd-${tone}`}>
      {children}
    </Element>
  )
}
