import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { Text } from '@headless/dashboard-sdk/components'
import { useState } from 'react'
import { CronPanel, deletedText, useCronJob } from '../cron-parts.tsx'
import { QueryState, StaleNotice } from '../parts.tsx'

export default function CronJobPage() {
  const cronJobId = useParams().cronJobId ?? ''
  const cronJob = useCronJob(cronJobId)
  // The delete's change makes the page read the cron job again and find it gone, which replaces the panel and its
  // outcome; the page keeps the outcome so the operator still sees how many occurrences went with it.
  const [deleted, setDeleted] = useState<string>()

  if (cronJob.error instanceof DashboardApiError && cronJob.error.status === 404) {
    return (
      <section aria-label="Recurring job not found">
        <p role="alert">This recurring job no longer exists.</p>
        {deleted !== undefined ? (
          <Text as="p" role="status" tone="hint">
            {deletedText(deleted)}
          </Text>
        ) : null}
        <Link to="/jobs/cron-jobs">Back to recurring jobs</Link>
      </section>
    )
  }

  return (
    <QueryState query={cronJob} label="Loading the recurring job" rows={3}>
      {(data) => (
        <>
          <StaleNotice what="this recurring job" onRefresh={() => void cronJob.refetch()} />
          <CronPanel cronJob={data} fires={data.nextFires} page onDeleted={setDeleted} />
        </>
      )}
    </QueryState>
  )
}
