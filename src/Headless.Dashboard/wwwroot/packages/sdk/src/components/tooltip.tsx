// The React Compiler's memo caches cost the SDK more bytes than this small wrapper saves, and the shell's assets sit
// near their size budget.
'use no memo'

import type { ReactElement, ReactNode } from 'react'
import { useFocusVisible } from 'react-aria/useFocusVisible'
import { Tooltip as AriaTooltip, TooltipTrigger } from 'react-aria-components'

export interface TooltipProps {
  /** What the tooltip says: the control's name, and under it why it is disabled, when it is. */
  content: ReactNode
  /** One React Aria control, such as a `Button`; wrap a plain element or a router link in `Focusable` first. */
  children: ReactElement
}

/** The look of a tooltip: inverted, so it reads over any surface, and wrapped at a readable measure. */
export const tooltipSurface = 'type-small z-(--z-popover) max-w-[32ch] rounded-md bg-foreground px-2 py-1 text-background'

/**
 * A tooltip under a control: it opens on hover after a short delay and at once on keyboard focus, so an icon-only
 * action still shows its name. React Aria flips it above the control and shifts it along the edge rather than let it
 * leave the viewport, so a long reason stays whole on a phone. The content is advisory; the control keeps its own
 * accessible name and description.
 */
export function Tooltip({ content, children }: TooltipProps) {
  // React Aria closes a tooltip on any key pressed on its control. A keyboard user keeps the one they focused until
  // they move on or press Esc (WCAG 1.4.13), so a key that moves nothing, such as an arrow at a grid's edge, does not
  // take the name away; a pointer press still closes it, and a click opens none.
  const { isFocusVisible } = useFocusVisible()

  return (
    <TooltipTrigger delay={400} closeDelay={0} shouldCloseOnPress={!isFocusVisible}>
      {children}
      <AriaTooltip placement="bottom" offset={4} containerPadding={8} className={tooltipSurface}>
        {content}
      </AriaTooltip>
    </TooltipTrigger>
  )
}
