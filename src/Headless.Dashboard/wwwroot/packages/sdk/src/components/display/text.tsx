'use no memo'

import type { ReactNode } from 'react'
import { useSectionHeading } from '../section-heading.ts'

/**
 * `hint` for secondary text, `faint` for a quiet aside, `label` for a small heading over a group, `title` for a
 * section heading, `error` for a failure, `footnote` for a mono note under a grid.
 */
export type TextTone = 'hint' | 'faint' | 'label' | 'title' | 'error' | 'footnote'

export interface TextProps {
  children: ReactNode
  tone: TextTone
  /**
   * The element, so a heading stays a heading and a paragraph a paragraph; `span` by default. `h3` is a section
   * heading inside a detail panel: on the object's own page, under its `h1`, it renders as `h2`.
   */
  as?: 'span' | 'p' | 'h2' | 'h3'
  /** `alert` for an error the operator must hear, `status` for a polite note. */
  role?: 'alert' | 'status'
}

/** A run of text in one of the dashboard's text tones, so a module never names a style. */
export function Text({ children, tone, as = 'span', role }: TextProps) {
  const section = useSectionHeading()
  const Element = as === 'h3' ? section : as

  return (
    <Element role={role} className={`hd-${tone}`}>
      {children}
    </Element>
  )
}
