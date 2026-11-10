import { useCallback, useState, useSyncExternalStore } from 'react'

interface Clock {
  now: number
  listeners: Set<() => void>
  timer: ReturnType<typeof setInterval> | undefined
}

// One timer per period for the whole page: a list of a hundred relative times re-renders on one tick, not on a hundred
// timers drifting apart.
const clocks = new Map<number, Clock>()

function clockOf(periodMs: number) {
  let clock = clocks.get(periodMs)

  if (!clock) {
    clock = { now: Date.now(), listeners: new Set(), timer: undefined }
    clocks.set(periodMs, clock)
  }

  return clock
}

function subscribe(periodMs: number, listener: () => void) {
  const clock = clockOf(periodMs)

  clock.listeners.add(listener)

  if (clock.timer === undefined) {
    clock.now = Date.now()
    clock.timer = setInterval(() => {
      clock.now = Date.now()
      clock.listeners.forEach((notify) => notify())
    }, periodMs)
  }

  return () => {
    clock.listeners.delete(listener)

    if (clock.listeners.size === 0) {
      clearInterval(clock.timer)
      clock.timer = undefined
    }
  }
}

/**
 * The current time, re-read every `periodMs` while a component shows it, so a relative time such as `in 7 sec.` moves
 * on, and turns into `due 3 sec. ago`, without waiting for new data. Components that pass the same period share one
 * timer. `periodMs` of `null` stops the ticking, for a time that will not change again.
 */
export function useNow(periodMs: number | null = 1_000) {
  const period = periodMs ?? 0
  // A stopped clock keeps the time it stopped at: a snapshot that changed on every read would re-render forever.
  const [stopped] = useState(Date.now)
  const subscribeTo = useCallback(
    (listener: () => void) => (period > 0 ? subscribe(period, listener) : () => undefined),
    [period],
  )

  return useSyncExternalStore(
    subscribeTo,
    () => (period > 0 ? clockOf(period).now : stopped),
    () => stopped,
  )
}

/** How often a relative time `at` needs re-reading: every second under a minute away, then every 30 s, then every minute. */
export function relativeTickOf(at: number, now = Date.now()) {
  const distance = Math.abs(at - now)

  return distance < 60_000 ? 1_000 : distance < 3_600_000 ? 30_000 : 60_000
}
