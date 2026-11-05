import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Vitest runs without globals, so Testing Library cannot register its own cleanup.
afterEach(() => {
  cleanup()
})

// jsdom lays nothing out; React Aria scrolls the focused row or option into view.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => undefined
}

// jsdom has no IntersectionObserver; a list with more pages watches its load-more row with one. Nothing intersects in
// a page that lays nothing out, so the stub never reports.
if (!('IntersectionObserver' in globalThis)) {
  globalThis.IntersectionObserver = class {
    readonly root = null
    readonly rootMargin = ''
    readonly thresholds = []
    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords() {
      return []
    }
  } as unknown as typeof IntersectionObserver
}
