// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useId, type ReactNode, type Ref } from 'react'
import { Button as AriaButton, composeRenderProps, type ButtonProps as AriaButtonProps } from 'react-aria-components'
import type { IconComponent } from '../icons/index.ts'
import { cn } from './cn.ts'
import { Tooltip, tooltipSurface } from './tooltip.tsx'

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
  /**
   * An icon before the label, from `@headless/dashboard-sdk/icons`. With no label the button is a square icon button,
   * and `aria-label` names it, since the icon is hidden from assistive technology.
   */
  icon?: IconComponent
  /**
   * What a tooltip says on hover and focus. An icon-only button shows its `aria-label` by default, and its disabled
   * reason under it; pass a shorter name when the label carries an id, such as `Copy id` for `Copy id 01a1…`.
   */
  tooltip?: ReactNode
}

// The variants are classes in sdk.css rather than utilities here, so the shell can put the same look on a plain link.
const variantClasses: Record<ButtonVariant, string> = {
  secondary: 'hd-btn',
  primary: 'hd-btn hd-btn-primary',
  danger: 'hd-btn hd-btn-danger',
  ghost: 'hd-btn hd-btn-ghost',
}

/** The dashboard's button: shadcn's, on React Aria's, so press, focus, and keyboard behave the same in every module. */
export function Button({
  variant = 'secondary',
  disabledReason,
  className,
  onPress,
  icon: Icon,
  tooltip,
  children,
  ...props
}: ButtonProps) {
  const reasonId = useId()
  const square = Icon != null && children == null
  const tip = tooltip ?? (square ? props['aria-label'] : undefined)
  const classes = composeRenderProps(className, (extra) => cn(variantClasses[variant], square && 'hd-icon', extra))
  // The icon takes the button's own text colour, so it reads as part of the label in every variant and state.
  const content = Icon
    ? composeRenderProps(children, (label) => (
        <>
          <Icon className="text-current" />
          {label}
        </>
      ))
    : children

  if (!disabledReason) {
    const button = (
      <AriaButton {...props} onPress={onPress} className={classes}>
        {content}
      </AriaButton>
    )

    return tip === undefined ? button : <Tooltip content={tip}>{button}</Tooltip>
  }

  // The reason is the button's description, so a screen reader reads it with the button. It stays in the page
  // rather than in a tooltip that mounts on hover, so the description always points at text; on screen it shows on
  // hover and focus, under the button, so it never widens or wraps the row the button sits in. A button with a
  // tooltip shows the reason there instead, under its name, and keeps the description hidden.
  const button = (
    <span className="group/why relative inline-flex">
      <AriaButton {...props} className={classes} aria-disabled="true" aria-describedby={reasonId} data-soft-disabled="">
        {content}
      </AriaButton>
      <span
        id={reasonId}
        className={
          tip === undefined
            ? `${tooltipSurface} pointer-events-none absolute end-0 top-[calc(100%+var(--sp-1))] w-max whitespace-normal opacity-0 group-focus-within/why:opacity-100 group-hover/why:opacity-100`
            : 'sr-only'
        }
      >
        {disabledReason}
      </span>
    </span>
  )

  return tip === undefined ? (
    button
  ) : (
    <Tooltip
      content={
        <>
          {tip}
          <span className="block text-background/80">{disabledReason}</span>
        </>
      }
    >
      {button}
    </Tooltip>
  )
}
