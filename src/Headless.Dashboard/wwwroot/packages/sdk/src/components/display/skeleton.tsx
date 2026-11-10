'use no memo'

/**
 * `rows` draws row-height bars for a list; `fields` a grid of label and value pairs for a field grid; `lines` short
 * text lines for prose or a payload; `chart` one chart-height block under a legend line.
 */
export type SkeletonShape = 'rows' | 'fields' | 'lines' | 'chart'

export interface SkeletonProps {
  /** What is loading, read out in place of the bars: `Loading the runs`. */
  label: string
  /** How many bars, pairs, or lines to draw. */
  rows?: number
  shape?: SkeletonShape
}

// The bars breathe on the same pulse as the Live dot, so a loading region reads as waiting rather than as a blank
// layout; reduced motion stops it with every other animation.
const bar = 'rounded-sm bg-muted animate-[hd-pulse_2s_ease-in-out_infinite]'

// Lines of varied width, so a block of them reads as text rather than as a stripe.
const lineWidths = ['w-4/5', 'w-3/5', 'w-11/12', 'w-2/5']

/**
 * The placeholder of a region while it loads, drawn in the shape of what will replace it; pair it with `useDelayed`
 * so a fast answer never flashes it.
 */
export function Skeleton({ label, rows = 3, shape = 'rows' }: SkeletonProps) {
  const count = Math.max(1, rows)
  let body

  if (shape === 'fields') {
    body = (
      <div className="grid grid-cols-[repeat(auto-fill,minmax(8rem,1fr))] gap-3">
        {Array.from({ length: count * 2 }, (_, index) => (
          <div key={index} className="flex flex-col gap-1.5">
            <div className={`${bar} h-2.5 w-14`} />
            <div className={`${bar} h-4 w-4/5`} />
          </div>
        ))}
      </div>
    )
  } else if (shape === 'lines') {
    body = (
      <div className="flex flex-col gap-2">
        {Array.from({ length: count }, (_, index) => (
          <div key={index} className={`${bar} h-3 ${lineWidths[index % lineWidths.length]}`} />
        ))}
      </div>
    )
  } else if (shape === 'chart') {
    body = (
      <div className="flex flex-col gap-2">
        <div className={`${bar} h-3 w-2/5`} />
        <div className={`${bar} h-(--chart-height) w-full`} />
      </div>
    )
  } else {
    body = (
      <div className="flex flex-col gap-2">
        {Array.from({ length: count }, (_, index) => (
          <div key={index} className={`${bar} h-(--row-height)`} />
        ))}
      </div>
    )
  }

  return (
    <div role="status" aria-busy="true" aria-label={label}>
      {body}
    </div>
  )
}
