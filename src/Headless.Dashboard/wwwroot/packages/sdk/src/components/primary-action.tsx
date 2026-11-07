import { createContext, useContext, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

const SlotContext = createContext<HTMLElement | null | undefined>(undefined)

/** Gives the views below the document bar's primary-action slot; the shell's layout provides it. */
export function PrimaryActionSlot({ element, children }: { element: HTMLElement | null; children: ReactNode }) {
  return <SlotContext.Provider value={element}>{children}</SlotContext.Provider>
}

/**
 * The view's one primary action, shown in the document bar while the view is mounted, so it leaves with the view.
 * Outside the shell (a view rendered on its own in a test) it renders in place.
 */
export function PrimaryAction({ children }: { children: ReactNode }) {
  const slot = useContext(SlotContext)

  if (slot === undefined) {
    return children
  }

  return slot ? createPortal(children, slot) : null
}
