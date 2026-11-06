// The React Compiler's memo caches cost the SDK more bytes than these rare renders save, and the shell's assets sit
// near their size budget.
'use no memo'

import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { Button } from '../button.tsx'
import type { DashboardStateRole } from '../../define-module.ts'
import { below, isBelow } from '../breakpoints.ts'
import { EditableValue, type FieldEdit } from '../frame/field-grid.tsx'
import { Mono, StatusMark } from '../marks.tsx'

export interface InspectorProps {
  /** Names the panel, such as `Run billing.charge`. */
  label: string
  /** Closes the inspector; on a phone this is the back control. */
  onClose?: () => void
  children: ReactNode
}

/**
 * The panel docked at the inline end that shows the selection, or the view's summary when nothing is selected.
 * Below 1280 px a selection's inspector (one with `onClose`) floats over the list as a drawer, and fills the screen
 * below 768 px; it then takes focus when it opens, `Esc` closes it, and focus returns to what opened it.
 */
export function Inspector({ label, onClose, children }: InspectorProps) {
  const ref = useRef<HTMLElement>(null)
  const closable = Boolean(onClose)

  useEffect(() => {
    const panel = ref.current

    if (!closable || !panel || !isBelow(below.wide)) {
      return undefined
    }

    const active = document.activeElement
    const opener = active instanceof HTMLElement && active !== document.body ? active : null
    panel.focus()

    return () => {
      const focused = document.activeElement

      // Focus goes back only when it would otherwise be lost with the panel, never away from where the operator moved it.
      if (opener?.isConnected && (!focused || focused === document.body || panel.contains(focused))) {
        opener.focus()
      }
    }
  }, [closable])

  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    // A dialog or popover above the inspector takes Esc first.
    if (event.key !== 'Escape' || !onClose || event.defaultPrevented || document.querySelector('[role="dialog"]')) {
      return
    }

    event.preventDefault()
    onClose()
  }

  return (
    <aside
      ref={ref}
      className={onClose ? 'hd-inspector hd-inspector-selection' : 'hd-inspector'}
      aria-label={label}
      tabIndex={onClose ? -1 : undefined}
      onKeyDown={onKeyDown}
    >
      {onClose ? (
        <Button variant="ghost" className="hd-inspector-close" onPress={onClose}>
          Close
        </Button>
      ) : null}
      {children}
    </aside>
  )
}

/** An editable field's value, how it commits, and what it shows while the operator types. */
export type InspectorFieldEdit = FieldEdit

export interface InspectorFieldProps {
  label: string
  /** The read-only value; an editable field shows `edit` instead. */
  children?: ReactNode
  edit?: InspectorFieldEdit
}

/**
 * One field in the inspector: a label and its value. An editable field commits on its own, with no Save: a select
 * on change, text on `Enter` or blur. Fields sit in an `InspectorFields` list.
 */
export function InspectorField({ label, children, edit }: InspectorFieldProps) {
  const id = useId()

  return (
    <>
      <dt>{edit ? <label htmlFor={id}>{label}</label> : label}</dt>
      <dd>{edit ? <EditableValue id={id} edit={edit} /> : children}</dd>
    </>
  )
}

export function InspectorFields({ children }: { children: ReactNode }) {
  return <dl className="hd-kv">{children}</dl>
}

export interface ObjectHeaderProps {
  title: string
  /** The object's id: shown mono, with copy. */
  id: string
  status?: { tone: DashboardStateRole; label: string }
  chips?: ReactNode
  /** The actions the server allows now. */
  actions?: ReactNode
}

/** An object's identity, status, chips, and actions, at the top of the inspector or an object page. */
export function ObjectHeader({ title, id, status, chips, actions }: ObjectHeaderProps) {
  return (
    <header className="hd-object-header">
      <h2 className="hd-title">{title}</h2>
      <span className="hd-object-id">
        <Mono>{id}</Mono>
        <Button variant="ghost" aria-label={`Copy ${id}`} onPress={() => void navigator.clipboard?.writeText(id)}>
          Copy
        </Button>
      </span>
      {status || chips ? (
        <span className="hd-chips">
          {status ? <StatusMark tone={status.tone} label={status.label} /> : null}
          {chips}
        </span>
      ) : null}
      {actions ? <div className="hd-actions">{actions}</div> : null}
    </header>
  )
}
