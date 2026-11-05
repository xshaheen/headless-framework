import type { ReactNode } from 'react'

export interface FigureProps {
  /** What the value counts: `Succeeded`. */
  label: string
  /** The value as the operator reads it: `98.2 %`. */
  value: ReactNode
  /** What the value is measured against: `−1.1 pt vs previous 24 h`. Required, because a bare number is not a figure. */
  comparison: ReactNode
}

/** A value with its comparison, such as `98.2 % succeeded, −1.1 pt vs previous 24 h`. */
export function Figure({ label, value, comparison }: FigureProps) {
  // The type already requires a comparison; this catches an empty one, or a caller outside TypeScript, while building.
  if (process.env.NODE_ENV !== 'production' && (comparison === undefined || comparison === null || comparison === '')) {
    throw new Error(`The figure "${label}" has no comparison. A bare number is not a figure: say what it is measured against.`)
  }

  return (
    <figure className="hd-figure">
      <figcaption className="hd-label">{label}</figcaption>
      <span className="hd-figure-value">{value}</span>
      <span className="hd-hint">{comparison}</span>
    </figure>
  )
}
