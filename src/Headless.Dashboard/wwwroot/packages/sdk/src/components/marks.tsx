import type { ReactNode } from 'react'

/** The five tones every status maps to. A new status picks one; it never adds a colour. */
export type StatusTone = 'healthy' | 'active' | 'waiting' | 'needs' | 'stopped'

/** A status as mark plus label: colour is never the only cue. */
export function StatusMark({ tone, label }: { tone: StatusTone; label: string }) {
  return (
    <span className={`hd-status hd-tone-${tone}`}>
      <span className="hd-mark" aria-hidden="true" />
      {label}
    </span>
  )
}

/** A small fact on an object, such as its tenant or node. Identifiers inside render mono and LTR. */
export function Chip({ label, value, mono }: { label?: string; value: ReactNode; mono?: boolean }) {
  return (
    <span className="hd-chip">
      {label ? <span>{label}</span> : null}
      <b className={mono ? 'hd-mono' : undefined}>{value}</b>
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
  return <kbd className="hd-kbd">{children}</kbd>
}

/** An identifier: mono, left-to-right, bidi-isolated. */
export function Mono({ children }: { children: ReactNode }) {
  return (
    <bdi className="hd-mono" dir="ltr">
      {children}
    </bdi>
  )
}
