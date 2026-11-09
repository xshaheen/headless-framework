'use no memo'

import type { ReactNode } from 'react'
import type { IconComponent } from '../../icons/index.ts'

export interface EmptyStateProps {
  /** What is empty, from `@headless/dashboard-sdk/icons`, before the text. */
  icon?: IconComponent
  /** What the place says when it holds nothing, and how to fill it. */
  children?: ReactNode
}

/** What a card or a list says when it holds nothing: one line, with an optional icon before it. */
export function EmptyState({ icon: Icon, children }: EmptyStateProps) {
  return Icon ? (
    <p className="hd-hint flex items-start gap-2 p-3.5">
      <Icon className="mt-px" />
      <span>{children}</span>
    </p>
  ) : (
    <p className="hd-hint p-3.5">{children}</p>
  )
}
