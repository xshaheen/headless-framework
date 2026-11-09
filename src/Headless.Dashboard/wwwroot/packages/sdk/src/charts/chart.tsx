// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Button } from '../components/button.tsx'
import type { Formatters } from '../locale/formatters.ts'
import { useFormat } from '../locale/locale.tsx'
import { ChartIcon, ListIcon } from '../icons/index.ts'
import {
  bucketRows,
  donutSentence,
  partRows,
  seriesColour,
  seriesVariables,
  timeSeriesSentence,
  type BucketRow,
  type ChartProps,
  type ChartTone,
  type DonutChartProps,
  type PartRow,
  type TimeSeriesChartProps,
} from './series.ts'

/**
 * A chart from a served series: lines, areas, stacked bars, or a donut, with a legend, a tooltip of exact values, and
 * a toggle to a list of the same data. The drawing is one image whose label is a sentence naming the series and the
 * range, because a screen reader gets nothing from the shapes; the list is the same data for anyone who wants it as
 * text. A bucket the server did not measure is a gap, never a zero.
 */
export function Chart(props: ChartProps) {
  return props.kind === 'donut' ? <DonutChart {...props} /> : <TimeSeriesChart {...props} />
}

function TimeSeriesChart(props: TimeSeriesChartProps) {
  const format = useFormat()
  const rows = bucketRows(props, format)

  return (
    <ChartFrame
      legend={props.series.map((item) => item.name)}
      tones={props.series.map((item) => item.tone)}
      sentence={timeSeriesSentence(props, rows, format)}
      drawing={<TimeSeriesDrawing props={props} rows={rows} format={format} />}
      list={<BucketList label={props.label} series={props.series.map((item) => item.name)} rows={rows} />}
    />
  )
}

function DonutChart(props: DonutChartProps) {
  const format = useFormat()
  const rows = partRows(props, format)
  const tones = props.parts.map((part) => part.tone)

  if (props.parts.every((part) => part.count.value === 0)) {
    return <p className="type-small text-muted-foreground">{props.empty ?? `No ${props.unit} in this range`}</p>
  }

  // Without exact, complete counts a slice's size would claim a share nobody measured, so the counts stand alone.
  if (rows.some((row) => row.share === null)) {
    return (
      <figure className="flex min-w-0 flex-col gap-2" style={seriesVariables(tones) as CSSProperties}>
        <PartList label={props.label} rows={rows} />
        <figcaption className="type-small text-faint">These counts are partial, so no shares are shown.</figcaption>
      </figure>
    )
  }

  return (
    <ChartFrame
      legend={rows.map((row) => `${row.name} ${row.share}`)}
      tones={tones}
      sentence={donutSentence(props, rows, format)}
      drawing={<DonutDrawing rows={rows} props={props} />}
      list={<PartList label={props.label} rows={rows} />}
    />
  )
}

interface ChartFrameProps {
  legend: readonly string[]
  tones: readonly ChartTone[]
  sentence: string
  drawing: ReactNode
  list: ReactNode
}

function ChartFrame({ legend, tones, sentence, drawing, list }: ChartFrameProps) {
  const [asList, setAsList] = useState(false)

  return (
    <figure className="flex min-w-0 flex-col gap-2" style={seriesVariables(tones) as CSSProperties}>
      {/* The toggle stays in one place in both modes, so the same button keeps focus when the view below it swaps. */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <ul className="flex flex-wrap gap-x-3 gap-y-1 type-small text-muted-foreground">
          {legend.map((name, index) => (
            <li key={name} className="flex items-center gap-1.5">
              <i className="size-2 rounded-xs" style={{ background: seriesColour(index) }} aria-hidden="true" />
              {name}
            </li>
          ))}
        </ul>
        <Button variant="ghost" icon={asList ? ChartIcon : ListIcon} className="ms-auto" onPress={() => setAsList(!asList)}>
          {asList ? 'Show chart' : 'Show list'}
        </Button>
      </div>
      {asList ? list : <div role="img" aria-label={sentence}>{drawing}</div>}
    </figure>
  )
}

/**
 * The drawing's size: measured from its box, which `--chart-height` sets, so Recharts draws to the pixel. A box that
 * measures nothing, before layout or outside a browser, falls back to a default width.
 */
function useBox() {
  const ref = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState({ width: 480, height: 140 })

  useLayoutEffect(() => {
    const node = ref.current

    if (!node) {
      return
    }

    const measure = () => {
      const rect = node.getBoundingClientRect()

      if (rect.width > 0 && rect.height > 0) {
        setBox((last) =>
          last.width === Math.round(rect.width) && last.height === Math.round(rect.height)
            ? last
            : { width: Math.round(rect.width), height: Math.round(rect.height) },
        )
      }
    }

    measure()

    if (typeof ResizeObserver === 'undefined') {
      return
    }

    const observer = new ResizeObserver(measure)
    observer.observe(node)

    return () => observer.disconnect()
  }, [])

  return [ref, box] as const
}

function TimeSeriesDrawing({ props, rows, format }: { props: TimeSeriesChartProps; rows: readonly BucketRow[]; format: Formatters }) {
  const [ref, box] = useBox()
  const data = rows.map((_, index) =>
    Object.fromEntries([['bucket', index], ...props.series.map((item, at) => [`s${at}`, item.points[index] ?? null])]),
  )
  const top = Math.max(
    0,
    ...rows.map((_, index) => {
      const values = props.series.map((item) => item.points[index] ?? 0)

      return props.kind === 'stacked-bar' ? values.reduce((sum, value) => sum + value, 0) : Math.max(0, ...values)
    }),
  )
  const count = (value: number) => format.number(value)
  // Recharts draws no axis: its axes measure text through a node it adds to the page. The labels are text beside the
  // drawing, as the SDK's other charts draw them, and the image's sentence already says the range.
  const shared = {
    width: box.width,
    height: box.height,
    data,
    margin: { top: 4, right: 0, bottom: 0, left: 0 },
    accessibilityLayer: false,
  }
  const axes = (
    <>
      <CartesianGrid vertical={false} stroke="var(--line)" />
      <XAxis dataKey="bucket" hide />
      <YAxis hide domain={[0, top || 1]} />
      <Tooltip
        isAnimationActive={false}
        cursor={{ stroke: 'var(--line)', fill: 'var(--hover)' }}
        content={({ active, label }) =>
          active && typeof label === 'number' && rows[label] ? (
            <TooltipBody title={rows[label].label} entries={props.series.map((item, at) => [item.name, rows[label]!.values[at]!])} />
          ) : null
        }
      />
    </>
  )

  const chart =
    props.kind === 'line' ? (
      <LineChart {...shared}>
        {axes}
        {props.series.map((item, at) => (
          <Line key={item.name} dataKey={`s${at}`} name={item.name} type="linear" stroke={seriesColour(at)} strokeWidth={1.5} dot={false} activeDot={{ r: 3, stroke: 'none', fill: seriesColour(at) }} isAnimationActive={false} />
        ))}
      </LineChart>
    ) : props.kind === 'area' ? (
      <AreaChart {...shared}>
        {axes}
        {props.series.map((item, at) => (
          <Area key={item.name} dataKey={`s${at}`} name={item.name} type="linear" stroke={seriesColour(at)} fill={seriesColour(at)} fillOpacity={0.16} strokeWidth={1.5} dot={false} activeDot={{ r: 3, stroke: 'none', fill: seriesColour(at) }} isAnimationActive={false} />
        ))}
      </AreaChart>
    ) : (
      <BarChart {...shared} barCategoryGap="20%">
        {axes}
        {props.series.map((item, at) => (
          <Bar key={item.name} dataKey={`s${at}`} name={item.name} stackId="stack" fill={seriesColour(at)} isAnimationActive={false} />
        ))}
      </BarChart>
    )

  return (
    <div className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-1 type-axis text-faint tabular-nums">
      <span className="flex flex-col justify-between text-end" aria-hidden="true">
        <span>{count(top)}</span>
        <span>{count(top / 2)}</span>
        <span>0</span>
      </span>
      <div ref={ref} className="h-(--chart-height) min-w-0">
        {chart}
      </div>
      <span className="col-start-2 flex justify-between" aria-hidden="true">
        <span>{rows[0]?.label}</span>
        <span>{rows.at(-1)?.label}</span>
      </span>
    </div>
  )
}

function DonutDrawing({ rows, props }: { rows: readonly PartRow[]; props: DonutChartProps }) {
  const [ref, box] = useBox()
  const size = Math.min(box.width, box.height)

  return (
    <div ref={ref} className="h-(--chart-height) min-w-0">
      <PieChart width={box.width} height={box.height} accessibilityLayer={false}>
        <Tooltip
          isAnimationActive={false}
          content={({ active, payload }) => {
            const at = typeof payload?.[0]?.payload?.index === 'number' ? (payload[0].payload.index as number) : -1
            const row = rows[at]

            return active && row ? <TooltipBody title={props.label} entries={[[row.name, `${row.count}, ${row.share}`]]} /> : null
          }}
        />
        <Pie
          data={props.parts.map((part, index) => ({ index, name: part.name, value: part.count.value }))}
          dataKey="value"
          nameKey="name"
          innerRadius={size * 0.32}
          outerRadius={size * 0.48}
          stroke="var(--panel)"
          isAnimationActive={false}
        >
          {props.parts.map((part, index) => (
            <Cell key={part.name} fill={seriesColour(index)} />
          ))}
        </Pie>
      </PieChart>
    </div>
  )
}

function TooltipBody({ title, entries }: { title: string; entries: readonly (readonly [string, string])[] }) {
  return (
    <div className="rounded-md border border-border bg-popover px-2 py-1.5 type-small text-popover-foreground shadow-lg">
      <p className="font-medium">{title}</p>
      <dl className="grid grid-cols-[auto_auto] gap-x-3 tabular-nums">
        {entries.map(([name, value]) => (
          <div key={name} className="contents">
            <dt className="text-muted-foreground">{name}</dt>
            <dd className="text-end">{value}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}

const listClasses = 'w-full type-mono-small tabular-nums [&_td]:border-t [&_td]:border-border [&_td]:py-1 [&_th]:py-1 [&_th]:font-medium [&_th]:text-muted-foreground'

function BucketList({ label, series, rows }: { label: string; series: readonly string[]; rows: readonly BucketRow[] }) {
  return (
    <div className="max-h-[calc(var(--chart-height)*2)] overflow-auto">
      <table aria-label={label} className={listClasses}>
        <thead>
          <tr>
            <th scope="col" className="text-start">Time</th>
            {series.map((name) => (
              <th key={name} scope="col" className="text-end">{name}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              <td>{row.label}</td>
              {row.values.map((value, at) => (
                <td key={at} className="text-end">{value}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function PartList({ label, rows }: { label: string; rows: readonly PartRow[] }) {
  const shares = rows.every((row) => row.share !== null)

  return (
    <table aria-label={label} className={listClasses}>
      <thead>
        <tr>
          <th scope="col" className="text-start">Part</th>
          <th scope="col" className="text-end">Count</th>
          {shares ? <th scope="col" className="text-end">Share</th> : null}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, index) => (
          <tr key={row.name}>
            <td>
              <span className="flex items-center gap-1.5">
                <i className="size-2 rounded-xs" style={{ background: seriesColour(index) }} aria-hidden="true" />
                {row.name}
              </span>
            </td>
            <td className="text-end">{row.count}</td>
            {shares ? <td className="text-end">{row.share}</td> : null}
          </tr>
        ))}
      </tbody>
    </table>
  )
}
