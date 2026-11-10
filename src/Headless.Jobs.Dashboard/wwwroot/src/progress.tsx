import { ProgressBar, Text } from '@headless/dashboard-sdk/components'
import { useFormat } from '@headless/dashboard-sdk/locale'
import { useEffect, useState } from 'react'
import { isFinished, type Run } from './runs.ts'

/** The current time, read again every second while `active`, so an age on screen keeps counting between changes. */
function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (!active) {
      return undefined
    }

    setNow(Date.now())
    const timer = setInterval(() => setNow(Date.now()), 1_000)

    return () => clearInterval(timer)
  }, [active])

  return now
}

export type ProgressRun = Pick<Run, 'function' | 'status' | 'progressPercent' | 'progressMessage' | 'progressUpdatedAt'>

/**
 * The progress a run's handler last reported, as the server stored it: a bar with its value and message, and how long
 * ago it was reported, counting up live, so a value the stream stopped refreshing reads as old rather than current. A
 * finished run keeps its last value as quiet text, without a bar, since nothing will move it again. `compact` fits a
 * one-line table cell or row note: the bar, the percentage, and the age share one line, and the message is left to
 * `progressText` for a hover title.
 */
export function RunProgress({ run, compact = false }: { run: ProgressRun; compact?: boolean }) {
  const format = useFormat()
  const finished = isFinished(run.status)
  const updatedAt = run.progressUpdatedAt
  const now = useNow(updatedAt !== undefined && !finished)
  const percent = run.progressPercent ?? 0
  const percentText = `${format.number(percent, { maximumFractionDigits: 1 })} %`
  const valueText = progressText(run, percentText)

  if (finished) {
    return <Text tone="hint">{compact ? percentText : valueText}</Text>
  }

  const age = updatedAt ? (
    <time dateTime={updatedAt} title={format.exactTime(updatedAt)}>
      {`updated ${format.relativeTime(updatedAt, { style: 'short', now })}`}
    </time>
  ) : null

  if (compact) {
    return (
      <ProgressBar
        label={`Progress of ${run.function}`}
        value={percent}
        max={100}
        valueText={valueText}
        shortText={percentText}
        inline
        detail={age}
      />
    )
  }

  return (
    <>
      <ProgressBar label={`Progress of ${run.function}`} value={percent} max={100} valueText={valueText} />
      {age ? <Text tone="faint">{age}</Text> : null}
    </>
  )
}

/** The percentage with the handler's message, as a compact cell's hover title shows it in full. */
export function progressText(run: Pick<Run, 'progressMessage'>, percentText: string) {
  return `${percentText}${run.progressMessage ? ` · ${run.progressMessage}` : ''}`
}
