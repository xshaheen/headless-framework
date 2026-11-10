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
 * finished run keeps its last value as quiet text, without a bar, since nothing will move it again.
 */
export function RunProgress({ run }: { run: ProgressRun }) {
  const format = useFormat()
  const finished = isFinished(run.status)
  const updatedAt = run.progressUpdatedAt
  const now = useNow(updatedAt !== undefined && !finished)
  const percent = run.progressPercent ?? 0
  const valueText = `${format.number(percent, { maximumFractionDigits: 1 })} %${run.progressMessage ? ` · ${run.progressMessage}` : ''}`

  if (finished) {
    return <Text tone="hint">{valueText}</Text>
  }

  return (
    <>
      <ProgressBar label={`Progress of ${run.function}`} value={percent} max={100} valueText={valueText} />
      {updatedAt ? (
        <Text tone="faint">
          <time dateTime={updatedAt} title={format.exactTime(updatedAt)}>
            {`updated ${format.relativeTime(updatedAt, { style: 'short', now })}`}
          </time>
        </Text>
      ) : null}
    </>
  )
}
