// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useId, type Ref } from 'react'
import { Button as AriaButton, type ButtonProps as AriaButtonProps } from 'react-aria-components'

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

function classes(variant: ButtonVariant, extra: AriaButtonProps['className']) {
  const base = `hd-btn hd-btn-${variant}`

  return typeof extra === 'function'
    ? (values: Parameters<typeof extra>[0]) => `${base} ${extra(values) ?? ''}`.trim()
    : `${base} ${extra ?? ''}`.trim()
}

/** The dashboard's button: React Aria's, so press, focus, and keyboard behave the same in every module. */
export function Button({ variant = 'secondary', disabledReason, className, onPress, ...props }: ButtonProps) {
  const reasonId = useId()

  if (!disabledReason) {
    return <AriaButton {...props} onPress={onPress} className={classes(variant, className)} />
  }

  return (
    <span className="hd-btn-group">
      <AriaButton
        {...props}
        className={classes(variant, className)}
        aria-disabled="true"
        aria-describedby={reasonId}
        data-soft-disabled=""
      />
      <span id={reasonId} className="hd-why">
        {disabledReason}
      </span>
    </span>
  )
}
