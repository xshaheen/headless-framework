// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
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
}

/** The scrim behind a modal; the modal sits high, so the palette's results grow downward. */
export const dialogScrim = 'fixed inset-0 z-(--z-dialog) grid items-start justify-items-center bg-scrim px-4 pt-[12vh] pb-4'

/** shadcn's dialog surface, with no entrance motion: the dashboard opens and closes overlays at once. */
export const dialogSurface = `flex max-h-[76vh] w-full max-w-(--dialog-width) flex-col overflow-auto rounded-lg ${floatingSurface}`

/**
 * A modal: only the command palette and action confirmations use one. Focus moves into it on open and returns to the
 * control that opened it on close.
 */
export function Dialog({ isOpen, onOpenChange, title, children, isDismissable = true }: OverlayProps) {
  return (
    <ModalOverlay
      className={dialogScrim}
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={isDismissable}
      isKeyboardDismissDisabled={!isDismissable}
    >
      <Modal className={dialogSurface}>
        <AriaDialog className="flex flex-col gap-3 p-5 outline-none">
          <Heading slot="title" className="type-detail">
            {title}
          </Heading>
          {children}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}
