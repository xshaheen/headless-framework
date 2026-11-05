import { Inspector } from '@headless/dashboard-sdk/components'
import { createContext, useContext, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

const DockContext = createContext<HTMLElement | null | undefined>(undefined)

export function DockProvider({ element, children }: { element: HTMLElement | null; children: ReactNode }) {
  return <DockContext.Provider value={element}>{children}</DockContext.Provider>
}

interface DockedInspectorProps {
  label: string
  /** Clears the selection; the drawer and the phone's full-screen inspector show it as Close. */
  onClose?: () => void
  children: ReactNode
}

/**
 * Shows a shell view's inspector in the layout's dock at the inline end: docked from 1280 px, a drawer below that,
 * and a full screen with Close on a phone. The view keeps owning what it shows.
 */
export function DockedInspector({ label, onClose, children }: DockedInspectorProps) {
  const dock = useContext(DockContext)

  if (dock === undefined) {
    throw new Error('DockedInspector must be rendered inside the shell layout, which owns the dock.')
  }

  return dock
    ? createPortal(
        <Inspector label={label} onClose={onClose}>
          {children}
        </Inspector>,
        dock,
      )
    : null
}
