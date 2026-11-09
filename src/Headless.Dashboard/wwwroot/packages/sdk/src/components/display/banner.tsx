// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'

export interface BannerProps {
  children: ReactNode
  /** `status` for a notice read out politely, such as `Stale`; leave it out when a child carries its own role. */
  role?: 'status'
  /** `nav` for a row of page links; `div` by default. */
  as?: 'div' | 'nav'
  /** The accessible name, which a `nav` needs. */
  'aria-label'?: string
}

/** A one-line notice above a view's content, with its action at the end: stale data, a failed load, missing sources. */
export function Banner({ children, role, as: Element = 'div', 'aria-label': label }: BannerProps) {
  return (
    <Element role={role} aria-label={label} className="hd-banner">
      {children}
    </Element>
  )
}
