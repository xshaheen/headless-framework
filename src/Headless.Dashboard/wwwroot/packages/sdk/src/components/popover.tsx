// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import { Dialog as AriaDialog, DialogTrigger, Popover as AriaPopover } from 'react-aria-components'
import { Button, type ButtonVariant } from './button.tsx'

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
      <AriaPopover className="z-(--z-popover) w-(--popover-width) rounded-lg border border-border bg-popover text-popover-foreground shadow-lg">
        <AriaDialog aria-label={label} className="flex flex-col gap-2.5 p-2.5 outline-none">
          {children}
        </AriaDialog>
      </AriaPopover>
    </DialogTrigger>
  )
}
