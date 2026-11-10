'use no memo'

import { useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
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
  ReferenceLine,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Button } from '../components/button.tsx'
import { floatingSurface } from '../components/cn.ts'
import { EmptyState } from '../components/display/empty-state.tsx'
import { Text } from '../components/display/text.tsx'
import type { Formatters } from '../locale/formatters.ts'
import { useFormat } from '../locale/locale.tsx'
import { ChartIcon, ListIcon } from '../icons/index.ts'
import {
  bucketName,
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
      list={<BucketList label={props.label} unit={props.unit} series={props.series.map((item) => item.name)} rows={rows} />}
    />
  )
}

function DonutChart(props: DonutChartProps) {
  const format = useFormat()
  const rows = partRows(props, format)
  const tones = props.parts.map((part) => part.tone)

  if (props.parts.every((part) => part.count.value === 0)) {
    return <EmptyState>{props.empty ?? `No ${props.unit} in this range`}</EmptyState>
  }

  // Without exact, complete counts a slice's size would claim a share nobody measured, so the counts stand alone.
  if (rows.some((row) => row.share === null)) {
    // Counts with no range say what they cover, since nothing else on the figure does.
    const covers = props.start === undefined || props.end === undefined ? `Counts of all ${props.unit}. ` : ''

    return (
      <figure className="flex min-w-0 flex-col gap-2" style={seriesVariables(tones) as CSSProperties}>
        <PartList label={props.label} unit={props.unit} rows={rows} />
        <figcaption className="type-small">
          <Text tone="faint">{`${covers}These counts are partial, so no shares are shown.`}</Text>
        </figcaption>
      </figure>
    )
  }

  return (
    <ChartFrame
      legend={rows.map((row) => `${row.name} ${row.share}`)}
      tones={tones}
      sentence={donutSentence(props, rows, format)}
      drawing={<DonutDrawing rows={rows} props={props} />}
      list={<PartList label={props.label} unit={props.unit} rows={rows} />}
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
              <Swatch index={index} />
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

/** A series' colour beside its name, in the legend and in the list. */
function Swatch({ index }: { index: number }) {
  return <i className="size-2 rounded-xs" style={{ background: seriesColour(index) }} aria-hidden="true" />
}

const fallbackBox = { width: 480, height: 140 }

/**
 * The drawing's size: measured from its box, which `--chart-height` sets, so Recharts draws to the pixel. Until the
 * first measurement the box is `null` and nothing is drawn, so the chart is not drawn once at a guessed size and again
 * at its own. A box that measures nothing, and every box without `ResizeObserver`, outside a browser, takes a default
 * size.
 */
function useBox() {
  const ref = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState<{ width: number; height: number } | null>(() =>
    typeof ResizeObserver === 'undefined' ? fallbackBox : null,
  )

  useLayoutEffect(() => {
    const node = ref.current

    if (!node) {
      return
    }

    const measure = () => {
      const rect = node.getBoundingClientRect()

      if (rect.width === 0 || rect.height === 0) {
        // A box that measures nothing, such as one inside a hidden tab, draws at the default size until it has one.
        setBox((last) => last ?? fallbackBox)
      } else {
        setBox((last) =>
          last && last.width === Math.round(rect.width) && last.height === Math.round(rect.height)
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

// The end date labels are not measured, so the present's mark claims the outer fifth of the axis at either end: a date
// label there would run into it, and the range's ends are already in the image's sentence.
const presentEdgeShare = 0.2

function TimeSeriesDrawing({ props, rows, format }: { props: TimeSeriesChartProps; rows: readonly BucketRow[]; format: Formatters }) {
  const [ref, box] = useBox()
  const { series, kind, present } = props
  const length = rows.length
  // The present's centre as a share of the axis; a range that ends today puts it on the last bucket.
  const presentAt = present && length > 0 ? (present.index + 0.5) / length : undefined
  const data = useMemo(
    () =>
      Array.from({ length }, (_, index) =>
        Object.fromEntries([['bucket', index], ...series.map((item, at) => [`s${at}`, item.points[index] ?? null])]),
      ),
    [series, length],
  )
  // An empty range still draws its grid from zero to one, so the labels read that scale instead of zero on every line.
  const top = useMemo(
    () =>
      Math.max(
        0,
        ...Array.from({ length }, (_, index) => {
          const values = series.map((item) => item.points[index] ?? 0)

          return kind === 'stacked-bar' ? values.reduce((sum, value) => sum + value, 0) : Math.max(0, ...values)
        }),
      ) || 1,
    [series, kind, length],
  )
  // Recharts draws no axis: its axes measure text through a node it adds to the page. The labels are text beside the
  // drawing, as the SDK's other charts draw them, and the image's sentence already says the range.
  const shared = {
    width: box?.width ?? 0,
    height: box?.height ?? 0,
    data,
    margin: { top: 4, right: 0, bottom: 0, left: 0 },
    accessibilityLayer: false,
  }
  const axes = (
    <>
      <CartesianGrid vertical={false} stroke="var(--line)" />
      <XAxis dataKey="bucket" hide />
      <YAxis hide domain={[0, top]} />
      <Tooltip
        isAnimationActive={false}
        cursor={{ stroke: 'var(--line)', fill: 'var(--hover)' }}
        content={({ active, label }) => {
          const row = active && typeof label === 'number' ? rows[label] : undefined

          return row ? <TooltipBody title={bucketName(row)} entries={props.series.map((item, at) => [item.name, row.values[at]!])} /> : null
        }}
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
        {present ? <ReferenceLine x={present.index} stroke="var(--edge)" strokeDasharray="3 3" /> : null}
        {props.series.map((item, at) => (
          <Bar key={item.name} dataKey={`s${at}`} name={item.name} stackId="stack" fill={seriesColour(at)} isAnimationActive={false}>
            {/* A bucket after the present counts work not yet run, so it is drawn outlined, as the schedule strip draws a future fire hollow. */}
            {present
              ? rows.map((row, index) => (
                  <Cell
                    key={index}
                    fill={seriesColour(at)}
                    fillOpacity={row.future ? 0 : 1}
                    stroke={row.future ? seriesColour(at) : 'none'}
                    strokeWidth={row.future ? 1 : 0}
                  />
                ))
              : null}
          </Bar>
        ))}
      </BarChart>
    )

  return (
    <div className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-1 type-axis text-faint tabular-nums">
      <span className="flex flex-col justify-between text-end" aria-hidden="true">
        <span>{format.number(top)}</span>
        <span>{format.number(top / 2)}</span>
        <span>0</span>
      </span>
      <div ref={ref} className="h-(--chart-height) min-w-0">
        {box ? chart : null}
      </div>
      {/* No wrapping: the present's mark is positioned near an edge, so its available width is a sliver, and a
          break-anywhere rule inherited from the host card would stack it one letter per line. */}
      <span className="relative col-start-2 flex justify-between whitespace-nowrap wrap-normal" aria-hidden="true">
        <span>{presentAt !== undefined && presentAt < presentEdgeShare ? null : rows[0]?.label}</span>
        {/* The present's mark sits under its bucket's centre, where the drawing's dashed line stands. It is pulled back by
            the same share of its own width, so it reaches the figure's edge at the first or last bucket and never past it. */}
        {presentAt !== undefined ? (
          <span
            className="absolute top-0 font-medium text-muted-foreground"
            style={{ left: `${presentAt * 100}%`, transform: `translateX(${-presentAt * 100}%)` }}
          >
            {present?.label}
          </span>
        ) : null}
        <span>{presentAt !== undefined && presentAt > 1 - presentEdgeShare ? null : rows.at(-1)?.label}</span>
      </span>
    </div>
  )
}

function DonutDrawing({ rows, props }: { rows: readonly PartRow[]; props: DonutChartProps }) {
  const [ref, box] = useBox()

  if (!box) {
    return <div ref={ref} className="h-(--chart-height) min-w-0" />
  }

  const size = Math.min(box.width, box.height)

  return (
    <div ref={ref} className="h-(--chart-height) min-w-0">
      <PieChart width={box.width} height={box.height} accessibilityLayer={false}>
        <Tooltip
          isAnimationActive={false}
          content={({ active, payload }) => {
            const row = rows.find((candidate) => candidate.name === payload?.[0]?.name)

            return active && row ? <TooltipBody title={props.label} entries={[[row.name, `${row.count}, ${row.share}`]]} /> : null
          }}
        />
        <Pie
          data={props.parts.map((part) => ({ name: part.name, value: part.count.value }))}
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
    <div className={`rounded-md px-2 py-1.5 type-small ${floatingSurface}`}>
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

// The list is a small data table: sans headers that never wrap, numbers alone in tabular mono at the inline end, and
// the unit said once in the caption rather than in every cell. Cells pad 8 between columns; the first and last sit on
// the card row's own inset, so the table lines up with the legend above it. Too wide for its card, it scrolls inside.
const listClasses =
  'w-full type-small tabular-nums [&_:is(th,td)]:px-2 [&_:is(th,td)]:whitespace-nowrap [&_:is(th,td):first-child]:ps-0 [&_:is(th,td):last-child]:pe-0 [&_td]:border-t [&_td]:border-border [&_td]:py-1 [&_th]:pb-1 [&_th]:type-label [&_th]:text-faint'
const numberCell = 'text-end type-mono-small text-foreground'

/** The unit, stated once for every number in the table. */
function ListCaption({ children }: { children: string }) {
  return <caption className="pb-1 text-start text-muted-foreground">{children}</caption>
}

/** A value the server did not measure: said in words, never a zero. */
function NumberCell({ value }: { value: string | null }) {
  return <td className={value === null ? 'text-end text-muted-foreground' : numberCell}>{value ?? 'not measured'}</td>
}

function BucketList({ label, unit, series, rows }: { label: string; unit: string; series: readonly string[]; rows: readonly BucketRow[] }) {
  return (
    <div className="max-h-[calc(var(--chart-height)*2)] min-w-0 overflow-auto">
      <table aria-label={label} className={listClasses}>
        <ListCaption>{`Values in ${unit}`}</ListCaption>
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
              <td className="type-mono-small text-foreground">{bucketName(row)}</td>
              {row.numbers.map((value, at) => (
                <NumberCell key={at} value={value} />
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function PartList({ label, unit, rows }: { label: string; unit: string; rows: readonly PartRow[] }) {
  const shares = rows.every((row) => row.share !== null)

  return (
    <div className="min-w-0 overflow-x-auto">
      <table aria-label={label} className={listClasses}>
        <ListCaption>{`Counts in ${unit}`}</ListCaption>
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
              <td className="text-foreground">
                <span className="flex items-center gap-1.5">
                  <Swatch index={index} />
                  {row.name}
                </span>
              </td>
              <NumberCell value={row.number} />
              {shares ? <NumberCell value={row.share} /> : null}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
