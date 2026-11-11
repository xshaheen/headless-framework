// The React Compiler's memo caches cost the SDK more bytes than this panel's renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Focusable } from 'react-aria-components'
import { Link, type To } from 'react-router'
import type { DashboardStateRole } from '../../define-module.ts'
import { CloseIcon, ExternalLinkIcon, MoveBelowIcon, MoveSideIcon } from '../../icons/index.ts'
import { Button } from '../button.tsx'
import { CopyButton } from '../copy-button.tsx'
import { SectionHeading } from '../section-heading.ts'
import { Tooltip } from '../tooltip.tsx'
import { useFrame } from './page-frame.tsx'

/** One tab of a detail panel; only the selected tab's content renders. */
export interface DetailTab {
  id: string
  label: string
  /** The tab's body. A tab with nothing to show (`null`) is left out, so a run without a chain has no Chain tab. */
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
  /**
   * The tab that opens first, by id, such as a failed run's Exception; the first tab when it is left out or has no
   * content. A tab the operator picked stays picked from one object to the next.
   */
  defaultTab?: string
  children?: ReactNode
  /** The actions the server allows now: primary, secondary, then destructive. */
  actions?: ReactNode
  /** The object's own page, for kinds that have one (run, cron job, function); adds Open in its own page. */
  detachTo?: To
}

const focusable = 'a[href], button:not([disabled]), input, select, textarea, [tabindex="0"]'

/**
 * The open object, docked under its list in a `PageFrame`: one title row (state dot, name, id with copy, tabs,
 * actions, then Open in its own page, Move to the side, and Close) over a body in two columns that each scroll. Tabs
 * are a tablist with roving focus. Below 768 px the panel is a full-screen modal that holds focus until it closes.
 */
export function DetailPanel({ label, name, id, status, tabs, defaultTab, children, actions, detachTo }: DetailPanelProps) {
  // Outside a frame the panel is the object's own page: nothing to close, move, or trap focus in.
  const frame = useFrame()
  const { close, stale, form, modal, side, setSide } = frame ?? { stale: false, form: false, modal: false, side: null }
  // On its own page the object's name is the page's heading; docked under a list, the list's title is.
  const Heading = frame ? 'h2' : 'h1'
  const ref = useRef<HTMLDivElement>(null)
  const base = useId()
  const [picked, setPicked] = useState<string>()
  const shown = tabs?.filter((item) => item.content != null && item.content !== false)
  const wanted = picked ?? defaultTab
  const index = Math.max(0, shown?.findIndex((item) => item.id === wanted) ?? 0)
  const tab = shown?.[index]

  useEffect(() => {
    if (modal) {
      ref.current?.focus()
    }
  }, [modal])

  function onTabKey(event: KeyboardEvent<HTMLDivElement>) {
    const count = shown?.length ?? 0
    const step = getComputedStyle(event.currentTarget).direction === 'rtl' ? -1 : 1
    const next = { ArrowRight: index + step, ArrowLeft: index - step, Home: 0, End: count - 1 }[event.key]

    if (next === undefined || !shown) {
      return
    }

    event.preventDefault()
    const target = (next + count) % count
    setPicked(shown[target]!.id)
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
      // `hd-panel` names the panel for the shell's canvas, which drops its padding around one.
      className="hd-panel relative flex min-w-0 flex-1 flex-col bg-card text-card-foreground focus:outline-none"
      aria-label={label}
      role={modal ? 'dialog' : 'region'}
      aria-modal={modal || undefined}
      tabIndex={modal ? -1 : undefined}
      onKeyDown={onKeyDown}
    >
      {/* The title row: the name and id first, then the tabs, actions, and window controls as one cluster that fills
          the rest of the row. The cluster grows from its content width (`flex-auto`, never a zero basis), so where
          the row is too narrow for both it drops to a second row whole instead of squeezing beside the title, and
          the window controls never land on a row of their own. On a phone the window controls leave the flow and keep
          the top end corner, where a sheet's close sits; the row pads past them so the name never runs under. */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 ps-4 pe-3 pt-2 max-md:pe-20">
        <div className="flex min-w-0 max-w-full items-center gap-2.5 max-md:flex-wrap max-md:gap-y-0.5">
          {status ? (
            <span className={`hd-tone-${status.tone} inline-flex items-center`}>
              <span className="hd-mark" aria-hidden="true" />
              <span className="sr-only">{status.label}</span>
            </span>
          ) : null}
          <Heading className={`whitespace-nowrap ${form ? 'type-detail' : 'type-detail-mono'}`}>{name}</Heading>
          {id ? (
            // One group, so the copy button never wraps onto a line of its own away from the id it copies. On a phone
            // the id takes its own line under the name and wraps there rather than truncate, so it reads whole.
            <span className="inline-flex min-w-0 items-center gap-1 max-md:basis-full">
              <bdi className="hd-mono hd-faint truncate max-md:whitespace-normal max-md:wrap-anywhere" dir="ltr">
                {id}
              </bdi>
              <CopyButton text={id} aria-label={`Copy id ${id}`} tooltip="Copy id" />
            </span>
          ) : null}
        </div>
        <div className="flex flex-auto flex-wrap items-center gap-x-3 gap-y-1">
          {shown ? (
            <div role="tablist" aria-label={label} className="flex" onKeyDown={onTabKey}>
              {shown.map((item, at) => (
                <button
                  key={item.id}
                  type="button"
                  role="tab"
                  className="h-(--row-height) cursor-pointer border-b-2 border-transparent px-3 type-tab text-faint transition-fast hover:text-foreground aria-selected:border-primary aria-selected:text-foreground"
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
          <span className="ms-auto flex flex-wrap items-center gap-1.5">{actions}</span>
          <span className="flex max-md:absolute max-md:top-2 max-md:end-3">
            {detachTo !== undefined ? (
              <Tooltip content="Open in its own page">
                <Focusable>
                  <Link to={detachTo} className="hd-btn hd-btn-ghost hd-icon" aria-label="Open in its own page">
                    <ExternalLinkIcon className="text-current" />
                  </Link>
                </Focusable>
              </Tooltip>
            ) : null}
            {side != null ? (
              <Button
                variant="ghost"
                icon={side ? MoveBelowIcon : MoveSideIcon}
                aria-label={side ? 'Move details below' : 'Move details to the side'}
                onPress={() => setSide?.(!side)}
              />
            ) : null}
            {close ? <Button variant="ghost" icon={CloseIcon} aria-label="Close details" onPress={close} /> : null}
          </span>
        </div>
      </div>
      {stale ? <p className="mx-4 mt-2 type-small text-muted-foreground">No longer matches the filter</p> : null}
      <div
        // A form's fields stop at a readable measure instead of stretching across a wide panel; each field then
        // sizes itself to what it holds through its `width`.
        className={`mt-2 grid min-h-0 flex-1 grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)] border-t border-border *:flex *:flex-col *:gap-3.5 *:overflow-auto *:px-4 *:py-3.5 *:only:col-span-full [&>:first-child:not(:only-child)]:border-e max-md:grid-cols-1 max-md:overflow-auto max-md:*:overflow-visible max-md:[&>:first-child:not(:only-child)]:border-e-0 max-md:[&>:first-child:not(:only-child)]:border-b${form ? ' [&_form]:max-w-[64ch] [&_section]:max-w-[64ch]' : ''}`}
        id={tab ? `${base}p` : undefined}
        role={tab ? 'tabpanel' : undefined}
        aria-labelledby={tab ? `${base}${index}` : undefined}
      >
        <SectionHeading value={frame ? 'h3' : 'h2'}>{tab ? tab.content : children}</SectionHeading>
      </div>
    </div>
  )
}
