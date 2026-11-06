// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useState, type KeyboardEvent } from 'react'
import { useFormat } from '../../locale/locale.tsx'
import type { DashboardStateRole } from '../../define-module.ts'
import { StatusMark } from '../marks.tsx'

/** One stored event that changed an object: what happened, when, and who did it where that is recorded. */
export interface TimelineEvent {
  id: string
  /** What happened: `Failed`, `Requeued`. */
  label: string
  /** The ISO time the server stored. */
  at: string
  /** The operator or node that caused it. */
  actor?: string
  /** The event's detail, such as an exception's first line or a note. */
  text?: string
  /** The status the event left the object in; the label shows as a status mark in that state role. */
  tone?: DashboardStateRole
}

export interface TimelineProps {
  label: string
  /** The events, in the order they happened. */
  events: readonly TimelineEvent[]
  selectedId?: string
  /** Puts an event in the inspector: a click, or `Enter` on the focused event. Focus stays in the timeline. */
  onSelect?: (id: string) => void
}

/**
 * An object's stored events as a WAI-ARIA grid: one tab stop, the arrow keys and `Home`/`End` move between events,
 * and `Enter` selects the focused one into the inspector without leaving the timeline.
 */
export function Timeline({ label, events, selectedId, onSelect }: TimelineProps) {
  const format = useFormat()
  const [focused, setFocused] = useState(0)
  const current = Math.min(focused, events.length - 1)

  function onKeyDown(event: KeyboardEvent<HTMLOListElement>) {
    const last = events.length - 1
    const to = { ArrowDown: current + 1, ArrowUp: current - 1, Home: 0, End: last }[event.key]

    if (event.key === 'Enter') {
      const selected = events[current]

      if (selected) {
        event.preventDefault()
        onSelect?.(selected.id)
      }

      return
    }

    if (to === undefined) {
      return
    }

    event.preventDefault()
    const next = Math.max(0, Math.min(last, to))
    setFocused(next)
    event.currentTarget.querySelectorAll<HTMLElement>('[role="row"]')[next]?.focus()
  }

  return (
    <ol role="grid" aria-label={label} className="hd-timeline" onKeyDown={onKeyDown}>
      {events.map((item, index) => (
        <li
          key={item.id}
          role="row"
          tabIndex={index === current ? 0 : -1}
          aria-selected={onSelect ? item.id === selectedId : undefined}
          onFocus={() => setFocused(index)}
          onClick={() => onSelect?.(item.id)}
        >
          <span role="gridcell">
            {item.tone ? <StatusMark tone={item.tone} label={item.label} /> : <strong>{item.label}</strong>}{' '}
            <time dateTime={item.at} title={format.exactTime(item.at)}>
              {format.relativeTime(item.at)}
            </time>
            {item.actor ? <span className="hd-hint">{` by ${item.actor}`}</span> : null}
            {item.text ? (
              <>
                {' '}
                <span className="hd-hint hd-timeline-text">{item.text}</span>
              </>
            ) : null}
          </span>
        </li>
      ))}
    </ol>
  )
}
