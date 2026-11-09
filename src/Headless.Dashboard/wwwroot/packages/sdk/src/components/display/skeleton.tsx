'use no memo'

export interface SkeletonProps {
  /** What is loading, read out in place of the bars: `Loading the runs`. */
  label: string
  /** How many row-height bars to draw. */
  rows?: number
}

/** The placeholder of a list or page while it loads; pair it with `useDelayed` so a fast answer never flashes it. */
export function Skeleton({ label, rows = 3 }: SkeletonProps) {
  return (
    <div role="status" aria-busy="true" aria-label={label} className="hd-fields">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="hd-skeleton" />
      ))}
    </div>
  )
}
