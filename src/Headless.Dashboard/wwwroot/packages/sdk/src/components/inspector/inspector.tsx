import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Button } from '../button.tsx'
import { Mono, StatusMark, type StatusTone } from '../marks.tsx'

export interface InspectorProps {
  /** Names the panel, such as `Run billing.charge`. */
  label: string
  /** Closes the inspector; on a phone this is the back control. */
  onClose?: () => void
  children: ReactNode
}

/** The widths at which a selection's inspector floats over the list as a drawer, as `components.css` lays it out. */
const floatingQuery = '(max-width: 1279px)'

function isFloating() {
  return typeof window.matchMedia === 'function' && window.matchMedia(floatingQuery).matches
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

    if (!closable || !panel || !isFloating()) {
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
export interface InspectorFieldEdit {
  value: string
  /**
   * Saves `value`. A rejection keeps the typed text uncommitted and shows the error's message inline, so the server's
   * validation reads where the operator typed.
   */
  onCommit: (value: string) => Promise<unknown> | void
  /** A select's choices; without them the field is text, committed on `Enter` or blur. */
  options?: readonly { value: string; label: string }[]
  /** Hears each keystroke without committing, such as to ask the server for a cron expression's next fires. */
  onDraftChange?: (value: string) => void
  /** What the typed text would mean, shown below the field before it commits. */
  preview?: ReactNode
}

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

function EditableValue({ id, edit }: { id: string; edit: InspectorFieldEdit }) {
  const { value, options, onDraftChange, preview } = edit
  const [draft, setDraft] = useState(value)
  const [seen, setSeen] = useState(value)
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  // The text last sent or refused: Enter then blur, or a blur after a refusal, does not send it again.
  const attempted = useRef<string>(undefined)

  // A committed value, or a live change, replaces the text once it arrives.
  if (seen !== value) {
    setSeen(value)
    setDraft(value)
  }

  async function commit(next: string) {
    if (next === value || next === attempted.current) {
      return
    }

    attempted.current = next
    setBusy(true)
    setError(undefined)

    try {
      await edit.onCommit(next)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not save. Try again.')
      // A select shows what is stored, not the refused option.
      if (options) {
        setDraft(value)
      }
    } finally {
      setBusy(false)
    }
  }

  const common = {
    id,
    className: 'hd-input',
    value: draft,
    'aria-invalid': error ? true : undefined,
    'aria-describedby': error ? `${id}-error` : undefined,
    'aria-busy': busy || undefined,
  }

  return (
    <span className="hd-field">
      {options ? (
        <select
          {...common}
          onChange={(event) => {
            attempted.current = undefined
            setDraft(event.currentTarget.value)
            void commit(event.currentTarget.value)
          }}
        >
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      ) : (
        <input
          {...common}
          onChange={(event) => {
            attempted.current = undefined
            setDraft(event.currentTarget.value)
            onDraftChange?.(event.currentTarget.value)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              void commit(draft)
            }
          }}
          onBlur={() => void commit(draft)}
        />
      )}
      {preview ? <span className="hd-hint">{preview}</span> : null}
      {error ? (
        <span id={`${id}-error`} role="alert" className="hd-error">
          {error}
        </span>
      ) : null}
    </span>
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
