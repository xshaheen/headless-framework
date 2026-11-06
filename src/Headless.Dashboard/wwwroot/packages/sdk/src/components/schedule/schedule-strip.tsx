import { useState, type KeyboardEvent } from 'react'
import { useFormat } from '../../locale/locale.tsx'
import { useViewKeys } from '../keymap.tsx'
import type { DashboardStateRole } from '../../define-module.ts'

/** One run or fire on a track. One at or before now is a past run, drawn filled; a later one is a fire, drawn hollow. */
export interface ScheduleTick {
  /** The object behind the tick, which `Enter` selects. */
  id: string
  /** The ISO instant the server computed, so a DST change shows as it happens, never as a fixed interval. */
  at: string
  /** The status as words, for the tick's accessible name: `failed`, `next fire`. */
  label: string
  /** The state role a past run paints in; a fire has no state yet and reads as scheduled. */
  tone?: DashboardStateRole
}

export interface ScheduleTrack {
  id: string
  /** The function, or the lane and topic: `billing.charge`. */
  label: string
  ticks: readonly ScheduleTick[]
}

export interface ScheduleStripProps {
  label: string
  tracks: readonly ScheduleTrack[]
  /** How far the window reaches each side of now, in ms; an hour by default. Keep it in the URL with `onSpanChange`. */
  span?: number
  onSpanChange?: (span: number) => void
  /** The zone the tick times read in, such as the scheduler's; the display time zone by default. */
  timeZone?: string
  /** Selects the object behind a tick: a click, or `Enter` on the focused tick. */
  onSelect?: (id: string) => void
  /** The instant the now line marks; the current time by default. */
  now?: number
}

const hour = 3_600_000
const minSpan = 60_000
const maxSpan = 7 * 24 * hour

/**
 * The window splits into this many equal slots, half each side of now, and the ticks in one slot merge into a mark
 * with a count. Fixed slots keep merged marks a slot apart, so their counts never run into one another, and never
 * merge a past run with a future fire.
 */
const slots = 24

/**
 * The schedule strip: one track per function or lane over a time axis with a now line, past runs filled in their
 * tone and future fires hollow. It is a grid: the arrow keys move between ticks and tracks, `Enter` selects the object
 * behind a tick, and `[`/`]` widen and narrow the window.
 */
export function ScheduleStrip({ label, tracks, span, onSpanChange, timeZone, onSelect, now = Date.now() }: ScheduleStripProps) {
  const format = useFormat()
  const [ownSpan, setOwnSpan] = useState(span ?? hour)
  const [focus, setFocus] = useState<[number, number]>([0, 0])
  const shown = span ?? ownSpan
  const start = now - shown
  const at = (instant: number) => (timeZone ? format.zonedTime(instant, timeZone) : format.exactTime(instant))

  function setSpan(next: number) {
    const bounded = Math.min(maxSpan, Math.max(minSpan, next))
    setOwnSpan(bounded)
    onSpanChange?.(bounded)
  }

  useViewKeys({ widen: () => setSpan(shown * 2), narrow: () => setSpan(shown / 2) })

  // Each track's marks: the ticks inside the window, merged where they would overlap.
  const rows = tracks.map((track) => {
    const marks: { slot: number; share: number; ticks: ScheduleTick[] }[] = []
    const inWindow = track.ticks
      .map((tick) => ({ tick, share: (Date.parse(tick.at) - start) / (2 * shown) }))
      .filter(({ share }) => share >= 0 && share <= 1)
      .sort((a, b) => a.share - b.share)

    for (const { tick, share } of inWindow) {
      const slot = Math.min(slots - 1, Math.floor(share * slots))
      const last = marks.at(-1)

      if (last?.slot === slot) {
        // A merged mark sits mid-slot, a slot from its neighbours, where its count has room.
        last.share = (slot + 0.5) / slots
        last.ticks.push(tick)
      } else {
        marks.push({ slot, share, ticks: [tick] })
      }
    }

    return marks
  })

  const [row, column] = rows[focus[0]]?.[focus[1]] ? focus : [rows.findIndex((marks) => marks.length > 0), 0]

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const mark = rows[row]?.[column]

    if (event.key === 'Enter' && mark) {
      event.preventDefault()
      onSelect?.(mark.ticks[0]!.id)
      return
    }

    const step = { ArrowRight: [0, 1], ArrowLeft: [0, -1], ArrowDown: [1, 0], ArrowUp: [-1, 0] }[event.key]
    const nextRow = row + (step?.[0] ?? 0)
    const marks = rows[nextRow]

    if (!step || !marks?.length) {
      return
    }

    event.preventDefault()
    const next: [number, number] = [nextRow, Math.max(0, Math.min(marks.length - 1, column + step[1]!))]
    setFocus(next)
    event.currentTarget.querySelector<SVGElement>(`[data-cell="${next[0]}-${next[1]}"]`)?.focus()
  }

  return (
    <div className="hd-strip">
      <div role="grid" aria-label={label} onKeyDown={onKeyDown}>
      {tracks.map((track, trackIndex) => (
        <div role="row" key={track.id} className="hd-strip-track">
          <span role="rowheader" className="hd-label">
            {track.label}
          </span>
          <svg role="presentation" width="100%" height="24">
            <line className="hd-strip-now" x1="50%" x2="50%" y1="0" y2="24" />
            {rows[trackIndex]!.map((mark, markIndex) => {
              const first = mark.ticks[0]!
              const count = mark.ticks.length
              const x = `${mark.share * 100}%`

              return (
                <g
                  key={first.id}
                  role="gridcell"
                  data-cell={`${trackIndex}-${markIndex}`}
                  tabIndex={trackIndex === row && markIndex === column ? 0 : -1}
                  aria-label={
                    count > 1
                      ? `${track.label}, ${count} ticks, ${at(Date.parse(first.at))} to ${at(Date.parse(mark.ticks.at(-1)!.at))}`
                      : `${track.label}, ${first.label}, ${at(Date.parse(first.at))}`
                  }
                  className={`hd-tone-${first.tone ?? 'sched'}${Date.parse(first.at) > now ? ' hd-strip-future' : ''}`}
                  onFocus={() => setFocus([trackIndex, markIndex])}
                  onClick={() => onSelect?.(first.id)}
                >
                  <rect x={x} y="5" width="3" height="14" />
                  {count > 1 ? (
                    <text x={x} dx="6" y="17">
                      {count}
                    </text>
                  ) : null}
                </g>
              )
            })}
          </svg>
        </div>
      ))}
      </div>
      <div className="hd-strip-track hd-hint">
        <span />
        <span className="hd-strip-axis">
          <span>{format.relativeTime(start, { now })}</span>
          <span>now</span>
          <span>{format.relativeTime(now + shown, { now })}</span>
        </span>
      </div>
    </div>
  )
}
