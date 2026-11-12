// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { ReactNode } from 'react'
import type { DashboardStateRole } from '../define-module.ts'

/**
 * A status as a dot in its state role's colour plus its label: colour is never the only cue, and the label keeps the
 * text tone, so states that share a role read apart.
 */
export function StatusMark({ tone, label }: { tone: DashboardStateRole; label: string }) {
  return (
    <span className={`hd-tone-${tone} inline-flex items-center gap-2 whitespace-nowrap`}>
      <span className="hd-mark" aria-hidden="true" />
      {label}
    </span>
  )
}

/** A small fact on an object, such as its tenant or node. Identifiers inside render mono and LTR. */
export function Chip({ label, value, mono }: { label?: string; value: ReactNode; mono?: boolean }) {
  return (
    <span className="inline-flex h-(--tag-pill-height) items-center gap-1 rounded-sm bg-muted px-1.5 type-label whitespace-nowrap text-muted-foreground">
      {label ? <span>{label}</span> : null}
      <b className={mono ? 'hd-mono font-medium text-foreground' : 'font-medium text-foreground'}>{value}</b>
    </span>
  )
}

export function TenantChip({ tenant }: { tenant: string }) {
  return <Chip label="tenant" value={tenant} mono />
}

export function NodeChip({ node }: { node: string }) {
  return <Chip label="node" value={node} mono />
}

/** A key cap in a shortcut hint: `⌘K`. Left to right, so a right-to-left page never reads a chord as `K⌘`. */
export function Kbd({ children }: { children: ReactNode }) {
  return <kbd dir="ltr" className="rounded-sm border border-b-2 border-border bg-card px-1 type-tag-mono text-faint">{children}</kbd>
}

/** An identifier: mono, left-to-right, bidi-isolated. */
export function Mono({ children }: { children: ReactNode }) {
  return (
    <bdi className="hd-mono" dir="ltr">
      {children}
    </bdi>
  )
}

/**
 * The class name of a .NET type without its namespace: `System.TimeoutException` reads `TimeoutException`. A generic
 * type's arguments carry dots of their own, so the name is read from what precedes them.
 */
export function shortTypeName(type: string) {
  const argumentsAt = type.indexOf('[')
  const name = argumentsAt < 0 ? type : type.slice(0, argumentsAt)

  return name.slice(name.lastIndexOf('.') + 1) || type
}

export interface FailureReasonProps {
  /** The exception type, with or without its namespace; without one, the reason is the message alone. */
  type?: string | null
  message?: string | null
}

/**
 * A failure in one line for a list cell: the exception type in the failure colour, without its namespace, then its
 * message. The whole text, namespace included, is the title, for a cell that had to shorten. It draws nothing without
 * a type and a message, so pass a row with neither `undefined` and let the table draw its blank.
 */
export function FailureReason({ type, message }: FailureReasonProps) {
  if (!type && !message) {
    return null
  }

  return (
    <span title={[type, message].filter(Boolean).join(': ')}>
      {type ? (
        <bdi className="hd-mono text-destructive" dir="ltr">
          {shortTypeName(type)}
        </bdi>
      ) : null}
      {type && message ? ': ' : null}
      {message}
    </span>
  )
}

/**
 * Splits a stored failure line, `Type: message`, into its parts. A line that does not open with a type name, such as
 * a reason the store wrote itself, is all message.
 */
export function splitFailure(line: string): { type?: string; message: string } {
  const match = /^([A-Za-z_][\w.`+]*(?:Exception|Error)): ?(.*)$/s.exec(line)

  return match ? { type: match[1], message: match[2] ?? '' } : { message: line }
}
