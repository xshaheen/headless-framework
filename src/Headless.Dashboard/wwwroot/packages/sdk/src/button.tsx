import { Button as AriaButton, type ButtonProps as AriaButtonProps } from 'react-aria-components'

export type ButtonProps = AriaButtonProps

/** The dashboard's button: React Aria's, so press, focus, and keyboard behave the same in every module. */
export function Button(props: ButtonProps) {
  return <AriaButton {...props} />
}
