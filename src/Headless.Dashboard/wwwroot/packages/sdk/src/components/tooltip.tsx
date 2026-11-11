// The React Compiler's memo caches cost the SDK more bytes than this small wrapper saves, and the shell's assets sit
// near their size budget.
'use no memo'

import type { ReactElement, ReactNode } from 'react'
import { Tooltip as AriaTooltip, TooltipTrigger } from 'react-aria-components'

export interface TooltipProps {
  /** What the tooltip says: the control's name, and under it why it is disabled, when it is. */
  content: ReactNode
  /** One React Aria control, such as a `Button`; wrap a plain element or a router link in `Focusable` first. */
  children: ReactElement
}

/** The look of a tooltip and of a disabled action's reason: inverted, so it reads over any surface. */
export const tooltipSurface = 'type-small z-(--z-popover) max-w-[32ch] rounded-md bg-foreground px-2 py-1 text-background'

/**
 * A tooltip under a control: it opens on hover after a short delay and at once on keyboard focus, so an icon-only
 * action still shows its name. The content is advisory; the control keeps its own accessible name and description.
 */
export function Tooltip({ content, children }: TooltipProps) {
  return (
    <TooltipTrigger delay={400} closeDelay={0}>
      {children}
      <AriaTooltip placement="bottom" offset={4} className={tooltipSurface}>
        {content}
      </AriaTooltip>
    </TooltipTrigger>
  )
}
