/** A toast: an outcome with no place on screen, or an inverse action such as Undo. Never the only home of an error. */
export interface Toast {
  id: number
  message: string
  action?: { label: string; onAction: () => void }
}

export interface ToastStore {
  show(toast: Omit<Toast, 'id'>): number
  dismiss(id: number): void
  getToasts(): readonly Toast[]
  subscribe(listener: () => void): () => void
}

export function createToastStore(): ToastStore {
  let toasts: readonly Toast[] = []
  let next = 1
  const listeners = new Set<() => void>()

  function set(value: readonly Toast[]) {
    toasts = value
    listeners.forEach((listener) => listener())
  }

  return {
    show(toast) {
      const id = next++
      set([...toasts, { ...toast, id }])

      return id
    },
    dismiss(id) {
      set(toasts.filter((toast) => toast.id !== id))
    },
    getToasts: () => toasts,
    subscribe(listener) {
      listeners.add(listener)

      return () => {
        listeners.delete(listener)
      }
    },
  }
}
