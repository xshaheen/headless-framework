import type { ReactNode } from 'react'
import { Dialog as AriaDialog, Heading, Modal, ModalOverlay } from 'react-aria-components'

export interface OverlayProps {
  isOpen: boolean
  onOpenChange: (isOpen: boolean) => void
  /** The heading, which also names the dialog for assistive technology. */
  title: string
  children: ReactNode
  /** Whether a press outside or `Esc` closes it. Off for a confirm whose outcome must be chosen. */
  isDismissable?: boolean
}

/**
 * A modal: only the command palette and action confirmations use one. Focus moves into it on open and returns to the
 * control that opened it on close.
 */
export function Dialog({ isOpen, onOpenChange, title, children, isDismissable = true }: OverlayProps) {
  return (
    <ModalOverlay
      className="hd-scrim"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={isDismissable}
      isKeyboardDismissDisabled={!isDismissable}
    >
      <Modal className="hd-dialog">
        <AriaDialog className="hd-dialog-body">
          <Heading slot="title" className="hd-dialog-title">
            {title}
          </Heading>
          {children}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}

/**
 * A panel from the inline-end edge: the inspector below 1280 px. Focus returns to the control that opened it on
 * close.
 */
export function Drawer({ isOpen, onOpenChange, title, children, isDismissable = true }: OverlayProps) {
  return (
    <ModalOverlay
      className="hd-scrim hd-scrim-drawer"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={isDismissable}
    >
      <Modal className="hd-drawer">
        <AriaDialog className="hd-drawer-body">
          <Heading slot="title" className="hd-dialog-title">
            {title}
          </Heading>
          {children}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  )
}
