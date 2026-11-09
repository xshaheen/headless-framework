'use no memo'

import type { ReactNode } from 'react'
import { Dialog as AriaDialog, DialogTrigger, Popover as AriaPopover } from 'react-aria-components'
import { Button, type ButtonVariant } from './button.tsx'
import { cn, floatingSurface } from './cn.ts'

export interface PopoverProps {
  /** Names the popover's dialog for assistive technology, such as `More actions`. */
  label: string
  /** The opening button's text, such as `More`. */
  trigger: ReactNode
  /** The opening button's look; secondary by default. */
  variant?: ButtonVariant
  children: ReactNode
}

/**
 * shadcn's popover: a button that opens a small floating dialog beside it, such as a panel's further actions. Focus
 * moves in on open, `Esc` closes it and returns focus to the button, and it opens without motion.
 */
export function Popover({ label, trigger, variant, children }: PopoverProps) {
  return (
    <DialogTrigger>
      <Button variant={variant}>{trigger}</Button>
      <AriaPopover className={cn('z-(--z-popover) w-(--popover-width) rounded-lg', floatingSurface)}>
        <AriaDialog aria-label={label} className="flex flex-col gap-2.5 p-2.5 outline-none">
          {children}
        </AriaDialog>
      </AriaPopover>
    </DialogTrigger>
  )
}
