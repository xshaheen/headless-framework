import { useSyncExternalStore } from 'react'

/**
 * The media conditions for the named breakpoints in `tokens.css` (`--bp-wide`, `--bp-medium`, `--bp-phone`). A media
 * condition cannot read a custom property, so script and CSS both write the widths as literals; each condition matches
 * the widths below its breakpoint, one pixel under it, as the CSS rules do.
 */
export const below = {
  wide: '(max-width: 1279px)',
  medium: '(max-width: 1023px)',
  phone: '(max-width: 767px)',
} as const

function media(query: string) {
  return typeof window.matchMedia === 'function' ? window.matchMedia(query) : null
}

// One listener set for every named breakpoint, so the subscription keeps its identity across renders and queries; a
// change at another breakpoint only re-reads the snapshot.
function subscribe(onChange: () => void) {
  const lists = Object.values(below).map(media)
  lists.forEach((list) => list?.addEventListener?.('change', onChange))

  return () => lists.forEach((list) => list?.removeEventListener?.('change', onChange))
}

/** Whether the window is below a named breakpoint, such as `below.phone`, as it resizes; `false` without `matchMedia`. */
export function useBelow(query: (typeof below)[keyof typeof below]) {
  return useSyncExternalStore(subscribe, () => isBelow(query), () => false)
}

/** Whether the window is below a named breakpoint now, read once; `false` without `matchMedia`. */
export function isBelow(query: (typeof below)[keyof typeof below]) {
  return Boolean(media(query)?.matches)
}

/**
 * Reads the browser's remembered `key`, or writes `value` under it. Storage can be missing or throw (a private window,
 * blocked site data); remembering is a convenience, so the read then answers `null` and the write does nothing.
 */
export function remember(key: string, value?: string) {
  try {
    if (value === undefined) {
      return localStorage.getItem(key)
    }

    localStorage.setItem(key, value)
  } catch {
    // Forgotten: see above.
  }

  return null
}
