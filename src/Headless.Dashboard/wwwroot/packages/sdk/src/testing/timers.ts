import { vi } from 'vitest'

// A few frames, because a frame callback may ask for the next one before the tick goes idle.
const drainMs = 3 * (1000 / 60)

/**
 * Puts real timers back after a test that faked them; call it instead of `vi.useRealTimers()`. jsdom drives animation
 * frames and its focus fix-up from one ticking interval, started with the global `setInterval` and stopped once nothing
 * is pending. A tick started while timers are fake lives on the fake clock, and `vi.useRealTimers()` drops it while
 * jsdom still holds its handle, so jsdom never starts another and no animation frame runs again in that test file.
 * Running the fake clock on a few frames first lets the tick finish its work and stop, so the next one starts on the
 * real clock.
 */
export async function restoreRealTimers(): Promise<void> {
  if (vi.isFakeTimers()) {
    await vi.advanceTimersByTimeAsync(drainMs)
  }

  vi.useRealTimers()
}
