// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import type { DashboardStateRole } from '../../define-module.ts'
import { useFormat } from '../../locale/locale.tsx'

export interface BarSeries {
  /** What the series counts: `Succeeded`. */
  name: string
  tone: DashboardStateRole
}

export interface BarColumn {
  /** The period the column covers: `09:00`, `Mon`. */
  label: string
  /** One value per series, in the series' order; the first stacks at the bottom. */
  values: readonly number[]
}

export interface BarChartProps {
  /** What the chart shows: `Outcomes per hour`. */
  label: string
  series: readonly BarSeries[]
  /** The served columns, oldest first. */
  columns: readonly BarColumn[]
  /** What one value counts, plural: `runs`. */
  unit: string
  /** The 36 px variant beside a row, with no axes; its label still says everything. */
  mini?: boolean
}

/**
 * Stacked bars from a served series, drawn to scale from zero. The SVG is one image whose label is a sentence that
 * names the series, the columns' range, and the tallest bar, because a screen reader gets nothing from the shapes.
 */
export function BarChart({ label, series, columns, unit, mini }: BarChartProps) {
  const format = useFormat()
  const count = (value: number) => format.count({ value, capped: false })
  const top = Math.max(0, ...columns.map((column) => column.values.reduce((sum, value) => sum + value, 0)))
  const scale = 100 / (top || 1)
  const names = series.map((item) => item.name).join(' and ')
  const sentence = `${label}: ${names}, from ${columns[0]?.label ?? ''} to ${columns.at(-1)?.label ?? ''}; the tallest bar is ${count(top)} ${unit}.`

  return (
    <div className={mini ? 'hd-bars hd-bars-mini' : 'hd-bars'}>
      {mini ? null : (
        <span className="hd-bars-y" aria-hidden="true">
          <span>{count(top)}</span>
          <span>{count(Math.round(top / 2))}</span>
          <span>0</span>
        </span>
      )}
      <svg role="img" aria-label={sentence} viewBox={`0 0 ${columns.length * 10} 100`} preserveAspectRatio="none">
        {columns.map((column, index) => {
          let base = 100

          return (
            <g key={column.label}>
              <title>{`${column.label}: ${column.values.map((value, at) => `${count(value)} ${series[at]?.name ?? ''}`).join(', ')}`}</title>
              {column.values.map((value, at) => {
                const height = value * scale
                base -= height

                return <rect key={at} className={`hd-tone-${series[at]?.tone ?? 'off'}`} x={index * 10 + 2} y={base} width={6} height={height} />
              })}
            </g>
          )
        })}
      </svg>
      {mini ? null : (
        <span className="hd-bars-x" aria-hidden="true">
          {columns.map((column) => (
            <span key={column.label}>{column.label}</span>
          ))}
        </span>
      )}
    </div>
  )
}
