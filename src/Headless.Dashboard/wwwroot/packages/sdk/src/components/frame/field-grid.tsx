// The React Compiler's memo caches cost the SDK more bytes than these small, rarely re-rendered parts save, and the
// shell's assets sit near their size budget.
'use no memo'

import { useId, useRef, useState, type ReactNode } from 'react'
import { inputClass } from '../form.tsx'

/** An editable field's value, how it commits, and what it shows while the operator types. */
export interface FieldEdit {
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

/** The control behind an editable field: it commits on its own, a select on change and text on `Enter` or blur. */
export function EditableValue({ id, edit }: { id: string; edit: FieldEdit }) {
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
    className: inputClass,
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

export interface FieldGridProps {
  /**
   * The most fields per row: 4 by default, 3 or 2 where values run long. The grid shows two on a narrow column and
   * widens to this many as the column it sits in allows, so the same grid reads in a side panel and across a wide one.
   */
  columns?: 2 | 3 | 4
  /** A heading over the group, such as `Outcome` or `Identity`, which also names it for assistive technology. */
  label?: string
  /** Prose under the grid, such as how a figure is counted. */
  footnote?: ReactNode
  /** `GridField`s. */
  children: ReactNode
}

// Container queries, not the viewport: a grid in a side panel is narrow on a wide screen.
const columnClasses: Record<NonNullable<FieldGridProps['columns']>, string> = {
  2: 'grid-cols-2',
  3: 'grid-cols-2 @md:grid-cols-3',
  4: 'grid-cols-2 @md:grid-cols-3 @xl:grid-cols-4',
}

/**
 * A detail panel's facts: a grid of labels over mono values in aligned columns, some editable, under an optional
 * heading, with an optional footnote. Several grids in one column read as groups, each after a hairline.
 */
export function FieldGrid({ columns = 4, label, footnote, children }: FieldGridProps) {
  const Element = label ? 'section' : 'div'

  return (
    <Element aria-label={label} className="@container not-first:border-t not-first:border-border not-first:pt-3.5">
      {label ? <h3 className="hd-label mb-2">{label}</h3> : null}
      <dl className={`m-0 grid ${columnClasses[columns]} gap-x-4 gap-y-3 type-mono wrap-anywhere`}>{children}</dl>
      {footnote ? <p className="hd-footnote">{footnote}</p> : null}
    </Element>
  )
}

export interface GridFieldProps {
  label: string
  /** The read-only value; an editable field shows `edit` instead. */
  children?: ReactNode
  edit?: FieldEdit
  /** `2` for a long value, such as a time or a correlation id. */
  span?: 1 | 2
  /** A mini action after the value, such as `Open trace`. */
  action?: ReactNode
}

/** One fact in a `FieldGrid`. An editable one commits on its own, with no Save. */
export function GridField({ label, children, edit, span, action }: GridFieldProps) {
  const id = useId()

  return (
    <div className={span === 2 ? 'hd-span-2' : undefined}>
      <dt className="hd-label">{edit ? <label htmlFor={id}>{label}</label> : label}</dt>
      <dd className="mt-0.5 flex flex-wrap items-center gap-2 tabular-nums">
        {edit ? <EditableValue id={id} edit={edit} /> : children}
        {action}
      </dd>
    </div>
  )
}
