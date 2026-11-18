// The React Compiler's memo caches cost the SDK more bytes than this small wrapper saves, and the shell's assets sit
// near their size budget.
'use no memo'

import type { Dispatch, ReactElement, ReactNode, SetStateAction } from 'react'
import { useContext, useEffect, useState } from 'react'
import { useFocusVisible } from 'react-aria/useFocusVisible'
import { flushSync } from 'react-dom'
import { Tooltip as AriaTooltip, TooltipContext, TooltipTrigger } from 'react-aria-components'

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
  const [isPointerPress, setIsPointerPress] = useState(false)
  // Once React Aria has rendered the switch to a pointer, its value is current again, so a later key keeps the tooltip.
  if (isPointerPress && !isFocusVisible) setIsPointerPress(false)

  return (
    <TooltipTrigger delay={400} closeDelay={0} shouldCloseOnPress={!isFocusVisible || isPointerPress}>
      {children}
      {isFocusVisible ? <PointerPressWatch onPointerPress={setIsPointerPress} /> : null}
      <AriaTooltip placement="bottom" offset={4} containerPadding={8} className={tooltipSurface}>
        {content}
      </AriaTooltip>
    </TooltipTrigger>
  )
}

/**
 * Renders a pointer press on the trigger before the trigger handles it. React Aria renders the switch from keyboard to
 * pointer only after the pointerdown that caused it, but the trigger reads `shouldCloseOnPress` during that
 * pointerdown, so a click after a key would leave a hover tooltip open or let a pending one open. A capture listener
 * runs before the press reaches the control. It renders synchronously only for a press on this trigger, because a
 * synchronous render flushes every pending update on the page.
 */
function PointerPressWatch({ onPointerPress }: { onPointerPress: Dispatch<SetStateAction<boolean>> }) {
  const context = useContext(TooltipContext)
  const triggerRef = context && 'triggerRef' in context ? context.triggerRef : undefined

  useEffect(() => {
    function onPointerDown(event: PointerEvent) {
      if (event.target instanceof Node && triggerRef?.current?.contains(event.target)) {
        flushSync(() => onPointerPress(true))
      }
    }

    document.addEventListener('pointerdown', onPointerDown, true)
    return () => document.removeEventListener('pointerdown', onPointerDown, true)
  }, [triggerRef, onPointerPress])

  return null
}
