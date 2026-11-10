import { useEffect, useState } from 'react'

/** Skeletons appear only after this long, so a fast answer never flashes one. */
const skeletonDelayMs = 150

/** `true` once `active` has held for `delayMs`; resets when it ends. */
export function useDelayed(active: boolean, delayMs = skeletonDelayMs) {
  const [shown, setShown] = useState(false)

  useEffect(() => {
    if (!active) {
      setShown(false)
      return undefined
    }

    const timer = setTimeout(() => setShown(true), delayMs)

    return () => clearTimeout(timer)
  }, [active, delayMs])

  return active && shown
}
