// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { IconComponent } from '../../icons/index.ts'

export interface CalloutProps {
  /** The callout's heading, such as `How it works`; without one the callout is its prose alone. */
  title?: string
  /** What the callout is about, from `@headless/dashboard-sdk/icons`, before the heading. */
  icon?: IconComponent
  /** Paragraphs, lists, or a definition list; the callout spaces them and draws the list markers. */
  children: ReactNode
}

/**
 * An explanation of several paragraphs beside a view's content, such as `How it works` or what a read-only view
 * cannot do. A one-line notice, a warning, or a failure is a `Banner` instead.
 */
export function Callout({ title, icon: Icon, children }: CalloutProps) {
  return (
    // A titled callout is a region named by its heading; an untitled one is plain prose with no landmark.
    <section className="hd-callout" aria-label={title}>
      {title ? (
        <h2 className="flex items-center gap-2 type-nav-strong text-foreground">
          {Icon ? <Icon /> : null}
          {title}
        </h2>
      ) : null}
      {children}
    </section>
  )
}
