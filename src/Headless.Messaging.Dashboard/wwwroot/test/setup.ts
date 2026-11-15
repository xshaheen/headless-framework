import { cleanup } from '@testing-library/react'
import { afterAll, afterEach } from 'vitest'

// Vitest runs without globals, so Testing Library cannot register its own cleanup.
afterEach(() => {
  cleanup()
})

// Recharts' Redux store queues each batched notification on an animation frame, with a 100 ms timer as a fallback that
// the frame cancels. Vitest closes jsdom after these hooks, which drops a pending frame and deletes the global
// cancelAnimationFrame, so a timer still queued then throws outside any test and fails the run. Frames run in the order
// they were requested, so once one more frame runs, every earlier one has cancelled its timer.
afterAll(async () => {
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
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
