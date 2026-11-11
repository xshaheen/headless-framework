// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useSyncExternalStore, type CSSProperties, type ReactNode } from 'react'
import { Dialog as AriaDialog, Heading, Modal, ModalOverlay } from 'react-aria-components'
import { CloseIcon } from '../icons/index.ts'
import { Button } from './button.tsx'
import { floatingSurface } from './cn.ts'

export interface OverlayProps {
  isOpen: boolean
  onOpenChange: (isOpen: boolean) => void
  /** The heading, which also names the dialog for assistive technology. */
  title: string
  children: ReactNode
  /** Whether a press outside or `Esc` closes it. Off for a confirm whose outcome must be chosen. */
  isDismissable?: boolean
  /** `alertdialog` when the dialog asks the operator to decide before going on, as an action's confirmation does. */
  role?: 'dialog' | 'alertdialog'
  /** The id of the text a screen reader reads after the title, such as what an action reaches. */
  'aria-describedby'?: string
}

/**
 * The scrim behind a modal; the modal sits high, so the palette's results grow downward. The gap above it and the
 * surface's height are fractions of the visual viewport rather than of the window, and the visual viewport loses the
 * keyboard's height while a phone's keyboard is up, so an open keyboard shrinks the dialog instead of covering it.
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

function viewportTop() {
  return window.visualViewport ? window.visualViewport.offsetTop : 0
}

function noViewportTop() {
  return 0
}

/**
 * The visual viewport's offset from the top of the window, as `--visual-viewport-top`. React Aria's overlay already
 * sets `--visual-viewport-height`; the offset matters because a phone browser scrolls the window under a modal to
 * bring a focused field into view, and a surface measured from the window's top would then sit partly off screen.
 */
export function useViewportStyle(): CSSProperties {
  const top = useSyncExternalStore(subscribeToViewport, viewportTop, noViewportTop)

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
 * A modal panel against the inline-start edge over a scrim, spanning the visual viewport's height: the shell's phone
 * navigation. While it is open Tab cycles inside it; its Close button, `Esc`, or a press on the scrim shuts it, and
 * focus goes back to whatever opened it. It holds navigation only, never an object to read or edit.
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
          {/* A visible way out for touch: the scrim is a sliver at phone width, and the control that opened the sheet
              sits under the scrim. */}
          <div className="flex justify-end px-2 pt-2">
            <Button variant="ghost" icon={CloseIcon} aria-label={`Close ${label}`} onPress={() => onOpenChange(false)} />
          </div>
          {children}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}
