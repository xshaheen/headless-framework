import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'

/** How close to the last loaded row the focus may come before the next page is asked for. */
export const loadAheadRows = 10

/**
 * Keeps what the operator reads in place while rows arrive above it: when the list's rows change, the first row in
 * view (or the next one still listed) stays at the same scroll offset, and the rows that arrived above it are held
 * there and counted. A row the operator scrolls back over counts as seen. `reveal` scrolls to the top and shows the
 * held rows; rows that `isRevealed` names, such as the one the operator just added and selected, are shown at once.
 */
export function useHeldRows(
  scrollRef: RefObject<HTMLElement | null>,
  ids: readonly string[],
  rowHeight: number,
  isRevealed: (id: string) => boolean,
) {
  const previous = useRef(ids)
  const revealNext = useRef(false)
  const [held, setHeld] = useState(0)

  useLayoutEffect(() => {
    const before = previous.current
    const element = scrollRef.current
    previous.current = ids
    const reveal = revealNext.current
    revealNext.current = false

    if (!element || before === ids || before.length === 0) {
      return
    }

    const top = element.scrollTop
    const positions = new Map(ids.map((id, index) => [id, index]))
    let from = Math.min(Math.floor(top / rowHeight), before.length - 1)
    let to: number | undefined

    for (; from < before.length; from++) {
      to = positions.get(before[from] ?? '')

      if (to !== undefined) {
        break
      }
    }

    if (to === undefined) {
      return
    }

    const listed = new Set(before)
    const arrived = ids.slice(0, to).filter((id) => !listed.has(id))

    if (reveal || arrived.some(isRevealed)) {
      element.scrollTop = 0
      setHeld(0)

      return
    }

    if (to !== from) {
      element.scrollTop = top + (to - from) * rowHeight
    }

    if (arrived.length > 0) {
      setHeld((count) => count + arrived.length)
    }
    // Runs when the rows change; the scroll offset and the reveal rule are read as they are then.
  }, [ids])

  useEffect(() => {
    const element = scrollRef.current

    if (!element) {
      return undefined
    }

    const onScroll = () => setHeld((count) => Math.min(count, Math.floor(element.scrollTop / rowHeight)))
    element.addEventListener('scroll', onScroll)

    return () => element.removeEventListener('scroll', onScroll)
  }, [scrollRef, rowHeight])

  return {
    held,
    /** Scrolls to the top so the held rows show; `andNext` also shows the rows the next change brings. */
    reveal(andNext = false) {
      revealNext.current = andNext

      if (scrollRef.current) {
        scrollRef.current.scrollTop = 0
      }

      setHeld(0)
    },
  }
}
