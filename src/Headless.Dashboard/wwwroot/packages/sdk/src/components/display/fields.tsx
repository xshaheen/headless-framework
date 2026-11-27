'use no memo'

import type { ReactNode, Ref } from 'react'

export interface FieldsProps {
  /** The section's accessible name; usually the text of the heading inside it. */
  label: string
  /** Optional ref for returning focus to a section, such as a lookup result. */
  ref?: Ref<HTMLElement>
  /** Optional tabIndex, such as `-1` when focus returns programmatically to the section. */
  tabIndex?: number
  children: ReactNode
}

/** A named section of stacked facts on a detail page, such as `Where it runs` or `Next fires`. */
export function Fields({ label, ref, tabIndex, children }: FieldsProps) {
  return (
    <section ref={ref} tabIndex={tabIndex} aria-label={label} className="hd-fields">
      {children}
    </section>
  )
}

/** A row of buttons that wraps on a narrow screen. */
export function Actions({ children }: { children: ReactNode }) {
  return <div className="hd-actions">{children}</div>
}
