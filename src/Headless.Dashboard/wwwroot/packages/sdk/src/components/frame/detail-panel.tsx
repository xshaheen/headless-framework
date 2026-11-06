// The React Compiler's memo caches cost the SDK more bytes than this panel's renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Link, type To } from 'react-router'
import type { DashboardStateRole } from '../../define-module.ts'
import { Button } from '../button.tsx'
import { useFrame } from './page-frame.tsx'

/** One tab of a detail panel; only the selected tab's content renders. */
export interface DetailTab {
  id: string
  label: string
  content: ReactNode
}

export interface DetailPanelProps {
  /** Names the region, such as `Run details`. */
  label: string
  /** The object's name, such as the job's function. */
  name: string
  /** The object's id, shown faint beside the name with a copy control. */
  id?: string
  status?: { tone: DashboardStateRole; label: string }
  /** The panel's tabs; the selected one is view state, never in the URL. Without tabs, `children` is the body. */
  tabs?: readonly DetailTab[]
  children?: ReactNode
  /** The actions the server allows now: primary, secondary, then destructive. */
  actions?: ReactNode
  /** The object's own page, for kinds that have one (run, cron job, function); adds Open in its own page. */
  detachTo?: To
}

function Glyph({ d }: { d: string }) {
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true">
      <path d={d} />
    </svg>
  )
}

const focusable = 'a[href], button:not([disabled]), input, select, textarea, [tabindex="0"]'

/**
 * The open object, docked under its list in a `PageFrame`: a title row (state dot, name, id with copy, tabs, actions,
 * then Open in its own page, Move to the side, and Close) over a body in two columns that each scroll. Tabs are a
 * tablist with roving focus. Below 768 px the panel is a full-screen modal that holds focus until it closes.
 */
export function DetailPanel({ label, name, id, status, tabs, children, actions, detachTo }: DetailPanelProps) {
  // Outside a frame the panel is the object's own page: nothing to close, move, or trap focus in.
  const { close, stale, form, modal, side, setSide } = useFrame() ?? { stale: false, form: false, modal: false, side: null }
  const ref = useRef<HTMLDivElement>(null)
  const base = useId()
  const [picked, setPicked] = useState<string>()
  const index = Math.max(0, tabs?.findIndex((tab) => tab.id === picked) ?? 0)
  const tab = tabs?.[index]

  useEffect(() => {
    if (modal) {
      ref.current?.focus()
    }
  }, [modal])

  function onTabKey(event: KeyboardEvent<HTMLDivElement>) {
    const count = tabs?.length ?? 0
    const step = getComputedStyle(event.currentTarget).direction === 'rtl' ? -1 : 1
    const next = { ArrowRight: index + step, ArrowLeft: index - step, Home: 0, End: count - 1 }[event.key]

    if (next === undefined || !tabs) {
      return
    }

    event.preventDefault()
    const target = (next + count) % count
    setPicked(tabs[target]!.id)
    event.currentTarget.querySelectorAll<HTMLElement>('[role="tab"]')[target]?.focus()
  }

  // The phone's full-screen panel is a modal: Tab and Shift+Tab wrap inside it.
  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const items = ref.current?.querySelectorAll<HTMLElement>(focusable)

    if (!modal || event.key !== 'Tab' || !items?.length) {
      return
    }

    const first = items[0]!
    const last = items[items.length - 1]!
    const active = document.activeElement

    if (event.shiftKey ? active === first || active === ref.current : active === last) {
      event.preventDefault()
      ;(event.shiftKey ? last : first).focus()
    }
  }

  return (
    <div
      ref={ref}
      className="hd-panel"
      aria-label={label}
      role={modal ? 'dialog' : 'region'}
      aria-modal={modal || undefined}
      tabIndex={modal ? -1 : undefined}
      onKeyDown={onKeyDown}
    >
      <div className="hd-panel-title">
        {status ? (
          <span className={`hd-tone-${status.tone} hd-status`}>
            <span className="hd-mark" aria-hidden="true" />
            <span className="hd-visually-hidden">{status.label}</span>
          </span>
        ) : null}
        <h2 className={form ? undefined : 'hd-mono'}>{name}</h2>
        {id ? (
          <>
            <bdi className="hd-mono hd-faint" dir="ltr">
              {id}
            </bdi>
            <Button
              variant="ghost"
              className="hd-icon"
              aria-label={`Copy id ${id}`}
              onPress={() => void navigator.clipboard?.writeText(id)}
            >
              <Glyph d="M5.5 5.5h8v8h-8zM2.5 10.5v-8h8" />
            </Button>
          </>
        ) : null}
        {tabs ? (
          <div role="tablist" aria-label={label} onKeyDown={onTabKey}>
            {tabs.map((item, at) => (
              <button
                key={item.id}
                type="button"
                role="tab"
                id={`${base}${at}`}
                aria-selected={at === index}
                aria-controls={`${base}p`}
                tabIndex={at === index ? 0 : -1}
                onClick={() => setPicked(item.id)}
              >
                {item.label}
              </button>
            ))}
          </div>
        ) : null}
        <span className="hd-panel-actions">{actions}</span>
        {detachTo !== undefined ? (
          <Link to={detachTo} className="hd-btn hd-btn-ghost hd-icon" aria-label="Open in its own page" title="Open in its own page">
            <Glyph d="M9 3h4v4M13 3L7.5 8.5M12 9.5V13H3V4h3.5" />
          </Link>
        ) : null}
        {side != null ? (
          <Button
            variant="ghost"
            className="hd-icon"
            aria-label={side ? 'Move details below' : 'Move details to the side'}
            onPress={() => setSide?.(!side)}
          >
            <Glyph d={side ? 'M2.5 2.5h11v11h-11zM2.5 9.5h11' : 'M2.5 2.5h11v11h-11zM9.5 2.5v11'} />
          </Button>
        ) : null}
        {close ? (
          <Button variant="ghost" className="hd-icon" aria-label="Close details" onPress={close}>
            <Glyph d="M3.5 3.5l9 9M12.5 3.5l-9 9" />
          </Button>
        ) : null}
      </div>
      {stale ? <p className="hd-panel-note">No longer matches the filter</p> : null}
      <div
        className="hd-panel-body"
        id={tab ? `${base}p` : undefined}
        role={tab ? 'tabpanel' : undefined}
        aria-labelledby={tab ? `${base}${index}` : undefined}
      >
        {tab ? tab.content : children}
      </div>
    </div>
  )
}
