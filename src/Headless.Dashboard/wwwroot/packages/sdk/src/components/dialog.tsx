// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useSyncExternalStore, type CSSProperties, type ReactNode } from 'react'
import { Dialog as AriaDialog, Heading, Modal, ModalOverlay } from 'react-aria-components'
import { floatingSurface } from './cn.ts'

export interface OverlayProps {
  isOpen: boolean
  onOpenChange: (isOpen: boolean) => void
  /** The heading, which also names the dialog for assistive technology. */
  title: string
  children: ReactNode
  /** Whether a press outside or `Esc` closes it. Off for a confirm whose outcome must be chosen. */
  isDismissable?: boolean
  /** `alertdialog` for a dialog that interrupts with a decision, such as an action's confirmation. */
  role?: 'dialog' | 'alertdialog'
  /** The id of the text a screen reader reads after the title, such as what an action reaches. */
  'aria-describedby'?: string
}

/**
 * The scrim behind a modal; the modal sits high, so the palette's results grow downward. Both the gap above it and the
 * surface's height are shares of the part of the screen left visible, which shrinks while a phone's keyboard is open,
 * so the keyboard never pushes a dialog off the screen.
 */
export const dialogScrim =
  'fixed inset-0 z-(--z-dialog) grid items-start justify-items-center bg-scrim px-4 pt-[calc(var(--visual-viewport-top,0px)+var(--visual-viewport-height,100dvh)*0.12)] pb-4'

/** shadcn's dialog surface, with no entrance motion: the dashboard opens and closes overlays at once. */
export const dialogSurface = `flex max-h-[calc(var(--visual-viewport-height,100dvh)*0.76)] w-full max-w-(--dialog-width) flex-col overflow-auto overscroll-contain rounded-lg ${floatingSurface}`

function subscribeToViewport(onChange: () => void) {
  const viewport = window.visualViewport

  viewport?.addEventListener('resize', onChange)
  viewport?.addEventListener('scroll', onChange)

  return () => {
    viewport?.removeEventListener('resize', onChange)
    viewport?.removeEventListener('scroll', onChange)
  }
}

/**
 * Where the visible part of the screen starts, for an overlay to place its surface in. React Aria sets the visible
 * height (`--visual-viewport-height`) but not this: a phone's browser can scroll the page under a modal to show a
 * focused field, and a surface placed from the overlay's top then starts above the screen.
 */
export function useViewportStyle(): CSSProperties {
  const top = useSyncExternalStore(
    subscribeToViewport,
    () => window.visualViewport?.offsetTop ?? 0,
    () => 0,
  )

  return { '--visual-viewport-top': `${top}px` } as CSSProperties
}

/**
 * A modal: only the command palette and action confirmations use one. Focus moves into it on open and returns to the
 * control that opened it on close.
 */
export function Dialog({
  isOpen,
  onOpenChange,
  title,
  children,
  isDismissable = true,
  role,
  'aria-describedby': describedBy,
}: OverlayProps) {
  return (
    <ModalOverlay
      className={dialogScrim}
      style={useViewportStyle()}
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={isDismissable}
      isKeyboardDismissDisabled={!isDismissable}
    >
      <Modal className={dialogSurface}>
        <AriaDialog role={role} aria-describedby={describedBy} className="flex flex-col gap-3 p-5 outline-none">
          <Heading slot="title" className="type-detail">
            {title}
          </Heading>
          {children}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}

export interface SheetProps {
  isOpen: boolean
  onOpenChange: (isOpen: boolean) => void
  /** What the sheet holds, which names it for assistive technology: `Jobs navigation`. */
  label: string
  children: ReactNode
}

/**
 * A modal panel at the inline start, over a scrim, as tall as the visible part of the screen: the shell's navigation
 * on a phone. Focus stays inside while it is open; `Esc` and a press on the scrim close it, and focus returns to the
 * control that opened it. It is navigation chrome, never a way to show or edit an object.
 */
export function Sheet({ isOpen, onOpenChange, label, children }: SheetProps) {
  return (
    <ModalOverlay
      className="fixed inset-0 z-(--z-dialog) bg-scrim"
      style={useViewportStyle()}
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable
    >
      <Modal className="fixed start-0 top-[var(--visual-viewport-top,0px)] flex h-[var(--visual-viewport-height,100dvh)] w-(--sidebar-phone-width) max-w-[85vw] flex-col border-e border-border bg-deep shadow-lg">
        <AriaDialog aria-label={label} className="flex min-h-0 flex-1 flex-col outline-none">
          {children}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}
