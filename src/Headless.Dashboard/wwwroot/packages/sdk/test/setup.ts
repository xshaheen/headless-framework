import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, vi } from 'vitest'

// Vitest runs without globals, so Testing Library cannot register its own cleanup.
//
// jsdom drives animation frames and focus fixup with one global setInterval that it starts on demand, and removing a
// focused element on unmount requests a fixup. While a test fakes timers that interval is a fake, so restoring real
// timers orphans it: jsdom still counts it as running, never starts a real one, and every later animation frame hangs.
// Running one frame after the unmount lets the fake interval finish and clear itself. The config runs hooks in
// definition order so this one runs before a test file's own afterEach restores real timers.
afterEach(() => {
  cleanup()
  if (vi.isFakeTimers()) {
    vi.advanceTimersByTime(1000 / 60)
  }
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
