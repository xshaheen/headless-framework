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
    <span className="inline-flex h-(--chip-height) items-center gap-1 rounded-md bg-muted px-1.5 type-label whitespace-nowrap text-muted-foreground">
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

/** A key cap in a shortcut hint: `⌘K`. */
export function Kbd({ children }: { children: ReactNode }) {
  return <kbd className="rounded-sm border border-b-2 border-border bg-card px-1 type-tag-mono text-faint">{children}</kbd>
}

/** An identifier: mono, left-to-right, bidi-isolated. */
export function Mono({ children }: { children: ReactNode }) {
  return (
    <bdi className="hd-mono" dir="ltr">
      {children}
    </bdi>
  )
}
