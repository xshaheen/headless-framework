// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { below } from '@headless/dashboard-sdk/components'
import { useEffect, useEffectEvent, useRef, useState, useSyncExternalStore, type ReactNode, type RefObject } from 'react'

/** Whether the window is below a named breakpoint now; the top bar folds its controls by it. */
export function useBelow(width: keyof typeof below) {
  const query = below[width]

  return useSyncExternalStore(
    (listener) => {
      const list = typeof matchMedia === 'function' ? matchMedia(query) : undefined
      list?.addEventListener('change', listener)

      return () => list?.removeEventListener('change', listener)
    },
    () => typeof matchMedia === 'function' && matchMedia(query).matches,
  )
}

/** An open-or-closed flag with the button that toggles it, for a flyout below the top bar. */
export function useFlyout() {
  const [isOpen, setOpen] = useState(false)
  const trigger = useRef<HTMLButtonElement>(null)

  return { isOpen, setOpen, trigger, toggle: () => setOpen((open) => !open) }
}

interface FlyoutProps {
  label: string
  isOpen: boolean
  onClose: () => void
  /** The button that opened it: a press on it toggles rather than counting as outside, and Escape returns focus there. */
  trigger: RefObject<HTMLElement | null>
  className?: string
  children: ReactNode
}

/**
 * A non-modal panel under the top bar, such as the Attention list or the account menu. It takes focus when it opens;
 * Escape or a press outside closes it, and Escape returns focus to its button. The page behind stays usable.
 */
export function Flyout({ label, isOpen, onClose, trigger, className, children }: FlyoutProps) {
  const panel = useRef<HTMLDivElement>(null)
  const close = useEffectEvent(onClose)

  useEffect(() => {
    if (!isOpen) {
      return undefined
    }

    panel.current?.focus()

    function onKey(event: KeyboardEvent) {
      const target = event.target as Node

      // Only Escape from the flyout, its button, or nowhere in particular: a dialog opened above it, such as the
      // palette, holds focus and closes first on its own Escape.
      if (event.key === 'Escape' && (target === document.body || panel.current?.contains(target) || trigger.current?.contains(target))) {
        // Taken before a list inside it sees it: Escape closes the nearest layer first.
        event.stopPropagation()
        close()
        trigger.current?.focus()
      }
    }

    function onPointer(event: PointerEvent) {
      const target = event.target as Node

      if (!panel.current?.contains(target) && !trigger.current?.contains(target)) {
        close()
      }
    }

    document.addEventListener('keydown', onKey, true)
    document.addEventListener('pointerdown', onPointer, true)

    return () => {
      document.removeEventListener('keydown', onKey, true)
      document.removeEventListener('pointerdown', onPointer, true)
    }
  }, [isOpen, trigger])

  return isOpen ? (
    <div ref={panel} role="dialog" aria-label={label} tabIndex={-1} className={`hd-flyout ${className ?? ''}`}>
      {children}
    </div>
  ) : null
}
