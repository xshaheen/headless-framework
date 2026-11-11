import { ProgressBar, Text } from '@headless/dashboard-sdk/components'
import { useFormat, useRelativeTime } from '@headless/dashboard-sdk/locale'
import { isFinished, type Run } from './runs.ts'

export type ProgressRun = Pick<Run, 'function' | 'status' | 'progressPercent' | 'progressMessage' | 'progressUpdatedAt'>

/**
 * The progress a run's handler last reported, as the server stored it: a bar with its value and message, and how long
 * ago it was reported, counting up live, so a value the stream stopped refreshing reads as old rather than current. A
 * finished run keeps its last value as quiet text, without a bar, since nothing will move it again. `compact` fits a
 * one-line table cell or row note: a running run's bar, percentage, and age share one line, its message stays in the
 * bar's accessible value and the cell's hover title, and a finished run's text is the same as in the full form.
 */
export function RunProgress({ run, compact = false }: { run: ProgressRun; compact?: boolean }) {
  const format = useFormat()
  const finished = isFinished(run.status)
  const updatedAt = run.progressUpdatedAt
  // The age counts up live, so a value the stream stopped refreshing reads as old; a finished run's age is not shown.
  const ago = useRelativeTime(finished ? '' : (updatedAt ?? ''))
  const percent = run.progressPercent ?? 0
  const percentText = `${format.number(percent, { maximumFractionDigits: 1 })} %`
  const valueText = progressText(run, percentText)

  // A finished run keeps its message in either form: nothing will move it again, and a one-line cell or note cuts it
  // with an ellipsis and its own hover title.
  if (finished) {
    return <Text tone="hint">{valueText}</Text>
  }

  // A one-line cell has room for the age alone; the full form says what the age measures.
  const age = updatedAt ? (
    <time dateTime={updatedAt} title={format.exactTime(updatedAt)}>
      {compact ? ago : `updated ${ago}`}
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
