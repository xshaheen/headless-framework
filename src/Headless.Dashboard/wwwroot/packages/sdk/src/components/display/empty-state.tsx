'use no memo'

import type { ReactNode } from 'react'
import type { IconComponent } from '../../icons/index.ts'

export interface EmptyStateProps {
  /** What is empty, from `@headless/dashboard-sdk/icons`, in a tile over the text. */
  icon?: IconComponent
  /** One line in the text tone over the explanation, for a state that names a fact: `This run no longer exists.` */
  title?: ReactNode
  /** What the place says when it holds nothing, and how it gets filled. */
  children?: ReactNode
  /** The next step, such as `Clear filters` or a link back to the list. */
  action?: ReactNode
  /**
   * `region`, the default, is a card, a chart, a tab, or a section; `page` stands in for a whole list or page and
   * takes more room above and below. Both draw the same icon, type, and measure.
   */
  placement?: 'region' | 'page'
  /** `alert` for an object that is gone, `status` for a polite note; no role for a plain empty list. */
  role?: 'alert' | 'status'
}

/**
 * What a list, a card, a chart, a tab, or a page says when it holds nothing: an optional icon in a tile, an optional
 * title, the explanation in the hint tone, and the next step. Every empty place is this one shape, centred in the
 * room it stands in for: it has one least height, so two empty cards are the same size, and inside a flex column it
 * takes what is left, so an empty card beside a full one fills its row with the state in the middle.
 */
export function EmptyState({ icon: Icon, title, children, action, placement = 'region', role }: EmptyStateProps) {
  return (
    <div
      role={role}
      className={`mx-auto flex w-full max-w-[46ch] flex-1 flex-col items-center justify-center gap-2.5 px-4 text-center ${
        placement === 'page' ? 'py-8' : 'min-h-36 py-5'
      }`}
    >
      {Icon ? (
        <span className="grid size-9 place-items-center rounded-lg bg-muted text-muted-foreground">
          <Icon />
        </span>
      ) : null}
      <div className="flex flex-col items-center gap-1">
        {title != null ? <p className="type-body text-foreground">{title}</p> : null}
        {/* The explanation can hold a block, such as a note with its own Retry, so it is a `div`: a `p` may not hold
            one. */}
        {children != null ? <div className="hd-hint text-pretty">{children}</div> : null}
        {action != null ? <div className="mt-1.5 flex flex-wrap items-center justify-center gap-2">{action}</div> : null}
      </div>
    </div>
  )
}
