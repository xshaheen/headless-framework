'use no memo'

import type { ReactNode } from 'react'
import type { IconComponent } from '../../icons/index.ts'

export interface EmptyStateProps {
  /** What is empty, from `@headless/dashboard-sdk/icons`, before the text. */
  icon?: IconComponent
  /** One line in the text tone over the explanation, for a state that names a fact: `This run no longer exists.` */
  title?: ReactNode
  /** What the place says when it holds nothing, and how it gets filled. */
  children?: ReactNode
  /** The next step, such as `Clear filters` or a link back to the list. */
  action?: ReactNode
  /** `page` centres the state in an otherwise empty page; `start`, the default, sits in a list or a card. */
  placement?: 'start' | 'page'
  /** `alert` for an object that is gone, `status` for a polite note; no role for a plain empty list. */
  role?: 'alert' | 'status'
}

/**
 * What a list, a card, a tab, or a page says when it holds nothing: an optional icon and title, the explanation in the
 * hint tone, and the next step. Start-aligned in a list or a card; centred only when it stands in for a whole page.
 */
export function EmptyState({ icon: Icon, title, children, action, placement = 'start', role }: EmptyStateProps) {
  const page = placement === 'page'

  return (
    <div
      role={role}
      className={
        page
          ? 'mx-auto flex max-w-[46ch] flex-col items-center gap-2 px-4 py-8 text-center'
          : 'flex items-start gap-2.5 p-3.5'
      }
    >
      {Icon ? <Icon className={page ? 'size-6 text-faint' : 'mt-px text-faint'} /> : null}
      <div className={page ? 'flex flex-col items-center gap-1' : 'flex min-w-0 flex-col gap-1'}>
        {title != null ? <p className="type-body text-foreground">{title}</p> : null}
        {/* The explanation can hold a block, such as a skeleton or a load error a card shows in its empty place, so
            it is a `div`: a `p` may not hold one. */}
        {children != null ? <div className="hd-hint">{children}</div> : null}
        {action != null ? <div className="mt-1 flex flex-wrap items-center gap-2">{action}</div> : null}
      </div>
    </div>
  )
}
