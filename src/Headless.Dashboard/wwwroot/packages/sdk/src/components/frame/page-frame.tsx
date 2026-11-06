// The React Compiler's memo caches cost the SDK more bytes than this frame's renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import {
  createContext,
  useContext,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  useSyncExternalStore,
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
  type RefObject,
} from 'react'
import { useSearchParams } from 'react-router'
import { below } from '../breakpoints.ts'
import { useViewKeys } from '../keymap.tsx'

export interface PageFrameOptions<T extends { id: string }> {
  /**
   * The view's stable id, such as `jobs.failed`. The panel's height is remembered per view under it, and the side
   * placement per product under its first segment.
   */
  viewId: string
  /** The rows in the list, in order; `J`/`K` walk them. */
  rows: readonly T[]
  /** Whether a row still matches the list's filter; an open row that stops matching stays open with a note. */
  matches?: (row: T) => boolean
}

/** A list view's open row, row cursor, and checked rows, as `usePageFrame` returns them for `PageFrame`. */
export interface PageFrameState {
  viewId: string
  /** The open row's id, from `?open=`; `null` when the panel is closed. */
  openId: string | null
  /** Opens a row in the panel and adds a history entry; focus stays where it is. */
  open: (id: string) => void
  /** Closes the panel, adding a history entry so Back reopens it; focus returns to the row that was open. */
  close: () => void
  /** The row `J`/`K` and `X` act on. */
  cursorId: string | null
  setCursor: (id: string) => void
  /** The bulk selection: view state that survives live updates but not a reload. */
  checked: ReadonlySet<string>
  setChecked: (ids: ReadonlySet<string>) => void
  /** The open row no longer matches the list's filter. */
  stale: boolean
  /** The list region; `PageFrame` attaches it, and rows inside carry `data-row-id` so the frame can focus them. */
  listRef: RefObject<HTMLDivElement | null>
}

const none: ReadonlySet<string> = new Set()

/**
 * The open row and the checked rows of a list view, for `PageFrame`. Only the open row is in the URL (`?open=<id>`).
 * `J`/`K` move the cursor and an open panel follows it, replacing the history entry; `X` checks the cursor row
 * without opening it; `Esc` closes, in order, a popover or menu (which handles it first), the panel, then the checked
 * rows. Rows in the list carry `data-row-id`; `Enter` on one opens it.
 */
export function usePageFrame<T extends { id: string }>({ viewId, rows, matches }: PageFrameOptions<T>): PageFrameState {
  const [params, setParams] = useSearchParams()
  const openId = params.get('open')
  const [cursor, setCursor] = useState<string | null>(null)
  const [checked, setChecked] = useState(none)
  const listRef = useRef<HTMLDivElement>(null)
  const shown = useRef(openId)
  const cursorId = cursor ?? openId
  const openRow = rows.find((row) => row.id === openId)

  function show(id: string | null, replace?: boolean) {
    setParams(
      (next) => {
        if (id) {
          next.set('open', id)
        } else {
          next.delete('open')
        }

        return next
      },
      { replace },
    )
  }

  function focusRow(id: string) {
    listRef.current?.querySelector<HTMLElement>(`[data-row-id="${CSS.escape(id)}"]`)?.focus()
  }

  function move(step: number) {
    const index = rows.findIndex((row) => row.id === cursorId)
    const row = rows[index === -1 ? 0 : index + step]

    if (!row) {
      return
    }

    setCursor(row.id)
    focusRow(row.id)

    if (openId) {
      show(row.id, true)
    }
  }

  const state: PageFrameState = {
    viewId,
    openId,
    open: (id) => {
      setCursor(id)

      if (id !== openId) {
        show(id)
      }
    },
    close: () => show(null),
    cursorId,
    setCursor,
    checked,
    setChecked,
    stale: Boolean(openRow && matches && !matches(openRow)),
    listRef,
  }

  // Taken over from the list's own keys, so the open panel follows the cursor and X checks rather than opens.
  useViewKeys({
    next: () => move(1),
    previous: () => move(-1),
    toggle: () => {
      if (cursorId) {
        const next = new Set(checked)

        if (!next.delete(cursorId)) {
          next.add(cursorId)
        }

        setChecked(next)
      }
    },
  })

  const escape = useRef<() => boolean>(() => false)
  escape.current = () => {
    if (openId) {
      show(null)
    } else if (checked.size > 0) {
      setChecked(none)
    } else {
      return false
    }

    return true
  }

  useEffect(() => {
    function onKeyDown(event: globalThis.KeyboardEvent) {
      // A popover, menu, or dialog handles its own Esc first; one still open when the key reaches here keeps it, so
      // one press never closes it and the panel together.
      if (
        event.key !== 'Escape' ||
        event.defaultPrevented ||
        document.querySelector('[role="dialog"]:not(.hd-panel), [role="alertdialog"], [role="menu"]')
      ) {
        return
      }

      if (escape.current()) {
        event.preventDefault()
      }
    }

    document.addEventListener('keydown', onKeyDown)

    return () => document.removeEventListener('keydown', onKeyDown)
  }, [])

  // Closing removes the panel and whatever had focus in it, so focus goes back to the row that was open; a close
  // from the list (Esc there) leaves focus where it is.
  useEffect(() => {
    const was = shown.current
    shown.current = openId
    const focused = document.activeElement

    if (was && !openId && (!focused || focused === document.body)) {
      focusRow(was)
    }
  })

  return state
}

interface FrameContext {
  close: () => void
  stale: boolean
  /** Below 768 px the panel is a full-screen modal. */
  modal: boolean
  /** At 1280 px and wider the panel can sit beside the list; `null` below. */
  side: boolean | null
  setSide: (side: boolean) => void
}

const Frame = createContext<FrameContext | null>(null)

/** The frame a `DetailPanel` sits in; a panel outside a `PageFrame` is a mistake caught at first render. */
export function useFrame() {
  const frame = useContext(Frame)

  if (!frame) {
    throw new Error('A DetailPanel renders only inside a PageFrame.')
  }

  return frame
}

function subscribe(onChange: () => void) {
  const queries = typeof window.matchMedia === 'function' ? [below.wide, below.phone].map((query) => window.matchMedia(query)) : []
  queries.forEach((query) => query.addEventListener?.('change', onChange))

  return () => queries.forEach((query) => query.removeEventListener?.('change', onChange))
}

function matchesMedia(query: string) {
  return typeof window.matchMedia === 'function' && window.matchMedia(query).matches
}

/** Below 1280 px: 1 (docked below only), below 768 px: 2 (modal), else 0. */
function widthClass() {
  return matchesMedia(below.phone) ? 2 : matchesMedia(below.wide) ? 1 : 0
}

// Browser storage can be missing or throw (a private window, blocked site data); the frame then forgets.
function read(key: string) {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Remembering is a convenience; the panel works without it.
  }
}

/** The panel's shortest height, and the least the list keeps above it. */
const minPanel = 200
const minList = 160

export interface PageFrameProps {
  frame: PageFrameState
  /** The stat strip, when the view has one. */
  stats?: ReactNode
  /** The page header: title, count, filters, selection cluster, primary action. */
  header?: ReactNode
  /** The list, usually a `DataTable`; it scrolls in its own region. */
  children: ReactNode
  /** The `DetailPanel` for the open row, or the add form; the panel region exists only while this is set. */
  panel?: ReactNode
  /** The panel holds a form: it opens taller and its handle reads `Resize form`. */
  form?: boolean
}

/**
 * A list-and-detail screen: the stat strip, the page header, the list in its own scroll region, and the detail panel
 * docked under the list behind a resize handle. The handle drags, or moves 16 px per arrow key (64 with Shift),
 * jumps to its limits on Home and End, and resets on double-click; the height is remembered per view. At 1280 px and
 * wider the panel can move to the side; below 768 px it opens full screen.
 */
export function PageFrame({ frame, stats, header, children, panel, form }: PageFrameProps) {
  const width = useSyncExternalStore(subscribe, widthClass, () => 0)
  const key = `hd-panel:${frame.viewId}`
  const sideKey = `hd-panel-side:${frame.viewId.split('.')[0]}`
  const initial = form ? 400 : 340
  const [height, setHeight] = useState(() => Number(read(key)) || initial)
  const [side, setSideState] = useState(() => read(sideKey) === '1')
  const [room, setRoom] = useState(0)
  const list = frame.listRef
  const box = useRef<HTMLDivElement>(null)
  const drag = useRef<{ y: number; height: number } | null>(null)
  const beside = side && width === 0
  const open = panel != null
  const max = Math.max(minPanel, room - minList)
  const value = Math.min(max, Math.max(minPanel, height))

  // The list and the panel share what is left under the header; measured again as the window resizes, so a
  // remembered height never squeezes the list below its minimum.
  useLayoutEffect(() => {
    function measure() {
      const shared = (list.current?.offsetHeight ?? 0) + (box.current?.offsetHeight ?? 0)

      if (shared > 0) {
        setRoom(shared)
      }
    }

    measure()
    window.addEventListener('resize', measure)

    return () => window.removeEventListener('resize', measure)
  }, [open, beside, list])

  function resize(next: number) {
    const clamped = Math.min(max, Math.max(minPanel, next))
    setHeight(clamped)
    write(key, String(clamped))
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const step = event.shiftKey ? 64 : 16
    const next = { ArrowUp: value + step, ArrowDown: value - step, Home: minPanel, End: max }[event.key]

    if (next !== undefined) {
      event.preventDefault()
      resize(next)
    }
  }

  function onPointerMove(event: PointerEvent<HTMLDivElement>) {
    if (drag.current) {
      resize(drag.current.height + drag.current.y - event.clientY)
    }
  }

  function onListKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const target = event.target as HTMLElement
    const id = target.closest<HTMLElement>('[data-row-id]')?.dataset.rowId

    if (event.key === 'Enter' && id && !event.defaultPrevented && !target.closest('button, a, input, select, textarea')) {
      frame.open(id)
    }
  }

  const context: FrameContext = {
    close: frame.close,
    stale: frame.stale,
    modal: width === 2,
    side: width === 0 ? beside : null,
    setSide: (next) => {
      setSideState(next)
      write(sideKey, next ? '1' : '')
    },
  }

  return (
    <Frame.Provider value={context}>
      <div className="hd-frame" data-side={(open && beside) || undefined}>
        <div className="hd-frame-main">
          {stats}
          {header}
          <div
            ref={list}
            className="hd-frame-list"
            onKeyDown={onListKeyDown}
            onFocus={(event) => {
              const id = (event.target as HTMLElement).closest<HTMLElement>('[data-row-id]')?.dataset.rowId

              if (id) {
                frame.setCursor(id)
              }
            }}
          >
            {children}
          </div>
        </div>
        {open && !beside && width !== 2 ? (
          <div
            role="separator"
            aria-orientation="horizontal"
            aria-label={form ? 'Resize form' : 'Resize details'}
            aria-valuenow={value}
            aria-valuemin={minPanel}
            aria-valuemax={max}
            tabIndex={0}
            className="hd-frame-handle"
            onKeyDown={onKeyDown}
            onDoubleClick={() => resize(initial)}
            onPointerDown={(event) => {
              event.currentTarget.setPointerCapture?.(event.pointerId)
              drag.current = { y: event.clientY, height: value }
            }}
            onPointerMove={onPointerMove}
            onPointerUp={() => (drag.current = null)}
          />
        ) : null}
        {open ? (
          <div ref={box} className="hd-frame-panel" style={beside || width === 2 ? undefined : { blockSize: value }}>
            {panel}
          </div>
        ) : null}
      </div>
    </Frame.Provider>
  )
}
