// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'
import { Tag } from '../display/tag.tsx'
import { Timestamp } from '../display/timestamp.tsx'
import { Text } from '../display/text.tsx'
import { StatusMark } from '../marks.tsx'

/** One stored event that changed an object: what happened, when, and who did it where that is recorded. */
export interface TimelineEvent {
  id: string
  /** What happened: `Failed`, `Requeued`. */
  label: string
  /** The ISO time the server stored. */
  at: string
  /** A time the work is due at rather than one it ran at: once passed it reads `due 42 sec. ago`. */
  scheduled?: boolean
  /** The operator or node that caused it. */
  actor?: string
  /** The event's detail, such as an exception's first line or a note. */
  text?: ReactNode
  /** The status the event left the object in; the label shows as a status mark in that state role. */
  tone?: DashboardStateRole
  /**
   * How deep the event sits in a tree, `0` for a root. Any event with a depth turns the timeline into a virtualized
   * treegrid: the events must then come in the tree's order, each parent before its children.
   */
  depth?: number
  /** Short notes on the event, such as why it sits where it does, shown as tags and read with the row. */
  tags?: readonly string[]
}

export interface TimelineProps {
  label: string
  /** The events, in the order they happened, or in the tree's order when they carry a depth. */
  events: readonly TimelineEvent[]
  selectedId?: string
  /** Puts an event in the inspector: a click, or `Enter` on the focused event. Focus stays in the timeline. */
  onSelect?: (id: string) => void
}

/** A tree row's fixed height in px: two lines, so the virtualizer can place a row without measuring it. */
const treeRowHeight = 56
/** Rows drawn beyond each edge of the viewport, so a fast scroll does not show a blank before the rows arrive. */
const overscan = 8
/** The viewport assumed before the tree has been laid out. */
const fallbackViewport = 800

/**
 * An object's stored events as a WAI-ARIA grid: one tab stop, the arrow keys and `Home`/`End` move between events,
 * and `Enter` selects the focused one into the inspector without leaving the timeline. Events that carry a depth
 * render as a treegrid instead, for a trace that may hold thousands of rows.
 */
export function Timeline(props: TimelineProps) {
  return props.events.some((event) => event.depth !== undefined) ? <TreeTimeline {...props} /> : <ListTimeline {...props} />
}

/** The arrow-key target from `current`, or `undefined` for a key that does not move. */
function moveTo(key: string, current: number, last: number) {
  const to = { ArrowDown: current + 1, ArrowUp: current - 1, Home: 0, End: last }[key]

  return to === undefined ? undefined : Math.max(0, Math.min(last, to))
}

function EventLine({ item }: { item: TimelineEvent }) {
  return (
    <>
      {item.tone ? <StatusMark tone={item.tone} label={item.label} /> : <strong>{item.label}</strong>}{' '}
      <Timestamp value={item.at} scheduled={item.scheduled} excludeFromTabOrder />
      {item.actor ? <Text tone="hint">{` by ${item.actor}`}</Text> : null}
    </>
  )
}

function ListTimeline({ label, events, selectedId, onSelect }: TimelineProps) {
  const [focused, setFocused] = useState(0)
  const current = Math.min(focused, events.length - 1)

  function onKeyDown(event: KeyboardEvent<HTMLOListElement>) {
    if (event.key === 'Enter') {
      const selected = events[current]

      if (selected) {
        event.preventDefault()
        onSelect?.(selected.id)
      }

      return
    }

    const next = moveTo(event.key, current, events.length - 1)

    if (next === undefined) {
      return
    }

    event.preventDefault()
    setFocused(next)
    event.currentTarget.querySelectorAll<HTMLElement>('[role="row"]')[next]?.focus()
  }

  return (
    <ol role="grid" aria-label={label} className="flex flex-col wrap-anywhere" onKeyDown={onKeyDown}>
      {events.map((item, index) => (
        <li
          key={item.id}
          role="row"
          className="cursor-default border-b border-border p-2 focus-visible:-outline-offset-2 aria-selected:bg-primary-soft"
          tabIndex={index === current ? 0 : -1}
          aria-selected={onSelect ? item.id === selectedId : undefined}
          onFocus={() => setFocused(index)}
          onClick={() => onSelect?.(item.id)}
        >
          <span role="gridcell">
            <EventLine item={item} />
            {item.text ? (
              <>
                {' '}
                <span className="hd-hint block">{item.text}</span>
              </>
            ) : null}
          </span>
        </li>
      ))}
    </ol>
  )
}

/**
 * The tree form: only the rows in view, and the focused one, are in the page; each row names its level, and the
 * indent stops at four levels on a phone, where deeper rows show their level as text.
 */
function TreeTimeline({ label, events, selectedId, onSelect }: TimelineProps) {
  const scroller = useRef<HTMLDivElement>(null)
  const [focused, setFocused] = useState(0)
  const [view, setView] = useState({ top: 0, height: fallbackViewport })
  // Set by a key that moved focus, so the row is focused once it is rendered.
  const pendingFocus = useRef(false)
  const current = Math.min(focused, events.length - 1)
  const last = events.length - 1
  const first = Math.max(0, Math.floor(view.top / treeRowHeight) - overscan)
  const end = Math.min(last, Math.ceil((view.top + view.height) / treeRowHeight) + overscan)
  const shown: number[] = []

  for (let index = first; index <= end; index++) {
    shown.push(index)
  }

  // The focused row stays in the page while scrolled away, so focus is never dropped to the body.
  if (current >= 0 && (current < first || current > end)) {
    shown.push(current)
  }

  function measure() {
    const element = scroller.current

    if (element) {
      setView({ top: element.scrollTop, height: element.clientHeight || fallbackViewport })
    }
  }

  // A window that grows, or a phone that turns, shows more rows without a scroll; jsdom has no observer.
  useLayoutEffect(() => {
    measure()

    if (typeof ResizeObserver === 'undefined' || !scroller.current) {
      return undefined
    }

    const observer = new ResizeObserver(measure)
    observer.observe(scroller.current)

    return () => observer.disconnect()
  }, [])

  useLayoutEffect(() => {
    if (pendingFocus.current) {
      pendingFocus.current = false
      scroller.current?.querySelector<HTMLElement>(`[aria-rowindex="${current + 1}"]`)?.focus()
    }
  }, [current])

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Enter') {
      const selected = events[current]

      if (selected) {
        event.preventDefault()
        onSelect?.(selected.id)
      }

      return
    }

    const next = moveTo(event.key, current, last)
    const element = scroller.current

    if (next === undefined || !element) {
      return
    }

    event.preventDefault()
    const height = element.clientHeight || fallbackViewport
    const rowTop = next * treeRowHeight

    // Scroll only as far as the row needs to come into view, as a browser does for a focused element.
    if (rowTop < element.scrollTop) {
      element.scrollTop = rowTop
    } else if (rowTop + treeRowHeight > element.scrollTop + height) {
      element.scrollTop = rowTop + treeRowHeight - height
    }

    measure()
    pendingFocus.current = true
    setFocused(next)
  }

  return (
    <div
      ref={scroller}
      role="treegrid"
      aria-label={label}
      aria-rowcount={events.length}
      className="relative min-h-0 flex-1 overflow-auto"
      onScroll={measure}
      onKeyDown={onKeyDown}
    >
      <div className="relative" style={{ height: events.length * treeRowHeight }}>
        {shown.map((index) => {
          const item = events[index]!
          const depth = item.depth ?? 0

          return (
            <div
              key={item.id}
              role="row"
              aria-level={depth + 1}
              aria-rowindex={index + 1}
              aria-selected={onSelect && selectedId !== undefined ? item.id === selectedId : undefined}
              tabIndex={index === current ? 0 : -1}
              className="absolute inset-x-0 cursor-default overflow-hidden border-b border-border py-1.5 pe-2 ps-[calc(0.5rem+min(var(--hd-depth),16)*1rem)] hover:bg-hover focus-visible:-outline-offset-2 aria-selected:bg-primary-soft max-md:ps-[calc(0.5rem+min(var(--hd-depth),4)*1rem)]"
              style={{ top: index * treeRowHeight, height: treeRowHeight, '--hd-depth': depth } as CSSProperties}
              onFocus={() => setFocused(index)}
              onClick={() => onSelect?.(item.id)}
            >
              <span role="gridcell" className="flex min-w-0 flex-col gap-0.5">
                <span className="flex min-w-0 items-center gap-1 overflow-hidden whitespace-nowrap">
                  {/* Every row says its level, as the indent shows it; past four the phone's indent stops, so the
                      level shows as text too. */}
                  <span className={depth >= 4 ? 'hd-hint' : 'sr-only'}>{`level ${depth + 1}`}</span>
                  <EventLine item={item} />
                </span>
                <span className="flex min-w-0 items-center gap-1.5 overflow-hidden whitespace-nowrap hd-hint">
                  {item.text ? <span className="truncate">{item.text}</span> : null}
                  {item.tags?.map((tag) => (
                    <Tag key={tag}>{tag}</Tag>
                  ))}
                </span>
              </span>
            </div>
          )
        })}
      </div>
    </div>
  )
}
