import type { ReactNode } from 'react'
import { Button } from '../button.tsx'
import { Mono, StatusMark, type StatusTone } from '../marks.tsx'

export interface InspectorProps {
  /** Names the panel, such as `Run billing.charge`. */
  label: string
  /** Closes the inspector; on a phone this is the back control. */
  onClose?: () => void
  children: ReactNode
}

/** The panel docked at the inline end that shows the selection, or the view's summary when nothing is selected. */
export function Inspector({ label, onClose, children }: InspectorProps) {
  return (
    <aside className="hd-inspector" aria-label={label}>
      {onClose ? (
        <Button variant="ghost" className="hd-inspector-close" onPress={onClose}>
          Close
        </Button>
      ) : null}
      {children}
    </aside>
  )
}

/** One read-only field in the inspector: a label and its value. Fields sit in an `InspectorFields` list. */
export function InspectorField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
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
  status?: { tone: StatusTone; label: string }
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
