// The React Compiler's memo caches cost the SDK more bytes than this small list saves, and the shell's assets sit near
// their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../../define-module.ts'

/** One event in an object's history: when, its state, and what happened. */
export interface HistoryItem {
  /** Stable across live updates; the index serves when the list only grows at its end. */
  key?: string
  /** A time already formatted for the display zone, absolute or relative, such as `−32 s`. */
  time: ReactNode
  tone: DashboardStateRole
  /** The state reached, such as `Failed`; it carries the meaning, so the dot beside it stays decorative. */
  state: string
  note?: ReactNode
  /** Row actions, such as an occurrence's Requeue and Delete; they stay in the tab order. */
  actions?: ReactNode
}

export interface HistoryListProps {
  /** The heading above the rows, such as `History` or `Occurrences`. */
  label: string
  items: readonly HistoryItem[]
}

/** An object's events in order, each row a time, a state dot, the state, and a faint note. */
export function HistoryList({ label, items }: HistoryListProps) {
  return (
    <section className="hd-history" aria-label={label}>
      <h3 className="hd-label">{label}</h3>
      <ol>
        {items.map((item, index) => (
          <li key={item.key ?? index} className={`hd-tone-${item.tone}`}>
            <span className="hd-history-time">{item.time}</span>
            <span className="hd-mark" aria-hidden="true" />
            <span>
              {item.state}
              {item.note ? <span className="hd-faint"> {item.note}</span> : null}
            </span>
            {item.actions}
          </li>
        ))}
      </ol>
    </section>
  )
}
