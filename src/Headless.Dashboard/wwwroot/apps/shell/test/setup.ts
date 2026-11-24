import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

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
  document.documentElement.removeAttribute('data-theme')
})

// jsdom lays nothing out; React Aria scrolls the focused row or option into view.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => undefined
}
