// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { cva } from 'class-variance-authority'
import { useId, type Ref } from 'react'
import { Button as AriaButton, composeRenderProps, type ButtonProps as AriaButtonProps } from 'react-aria-components'
import { cn } from './cn.ts'

export type ButtonVariant = 'secondary' | 'primary' | 'danger' | 'ghost'

export interface ButtonProps extends AriaButtonProps {
  /** The rendered button, such as for a popover that returns focus to the button that opened it. */
  ref?: Ref<HTMLButtonElement>
  /** `primary` for the view's one primary action; `danger` for a destructive verb. */
  variant?: ButtonVariant
  /**
   * Why the action is not available now, such as `Needs operate permission`. The button stays focusable and shows
   * the reason beside it, so the operator learns why on hover and focus; pressing it does nothing.
   */
  disabledReason?: string | null
}

// The variants are classes in sdk.css rather than utilities here, so the shell can put the same look on a plain link.
const buttonVariants = cva('hd-btn', {
  variants: {
    variant: { secondary: '', primary: 'hd-btn-primary', danger: 'hd-btn-danger', ghost: 'hd-btn-ghost' },
  },
})

/** The dashboard's button: shadcn's, on React Aria's, so press, focus, and keyboard behave the same in every module. */
export function Button({ variant = 'secondary', disabledReason, className, onPress, ...props }: ButtonProps) {
  const reasonId = useId()
  const classes = composeRenderProps(className, (extra) => cn(buttonVariants({ variant }), extra))

  if (!disabledReason) {
    return <AriaButton {...props} onPress={onPress} className={classes} />
  }

  // The reason is the button's description, so a screen reader reads it with the button. It stays in the page
  // rather than in a tooltip that mounts on hover, so the description always points at text; on screen it shows on
  // hover and focus, under the button, so it never widens or wraps the row the button sits in.
  return (
    <span className="group/why relative inline-flex">
      <AriaButton {...props} className={classes} aria-disabled="true" aria-describedby={reasonId} data-soft-disabled="" />
      <span
        id={reasonId}
        className="type-small pointer-events-none absolute end-0 top-[calc(100%+var(--sp-1))] z-(--z-popover) w-max max-w-[32ch] rounded-md bg-foreground px-2 py-1 whitespace-normal text-background opacity-0 group-focus-within/why:opacity-100 group-hover/why:opacity-100"
      >
        {disabledReason}
      </span>
    </span>
  )
}
