import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { EmptyState, usePageTitle } from '@headless/dashboard-sdk/components'
import { RecurringIcon } from '@headless/dashboard-sdk/icons'
import { useState } from 'react'
import { CronPanel, deletedText, useCronJob } from '../cron-parts.tsx'
import { QueryState, StaleNotice } from '../parts.tsx'

export default function CronJobPage() {
  const cronJobId = useParams().cronJobId ?? ''
  const cronJob = useCronJob(cronJobId)
  // The delete's change makes the page read the cron job again and find it gone, which replaces the panel and its
  // outcome; the page keeps the outcome so the operator still sees how many occurrences went with it.
  const [deleted, setDeleted] = useState<string>()
  const isGone = cronJob.error instanceof DashboardApiError && cronJob.error.status === 404

  usePageTitle(
    isGone
      ? 'Recurring job not found'
      : cronJob.data
        ? `${cronJob.data.function} recurring job`
        : cronJob.error
          ? 'Recurring job'
          : undefined,
  )

  if (isGone) {
    return (
      <section aria-label="Recurring job not found">
        <EmptyState
          role="alert"
          placement="page"
          icon={RecurringIcon}
          title="This recurring job no longer exists."
          action={<Link to="/jobs/cron-jobs">Back to recurring jobs</Link>}
        >
          {deleted !== undefined ? deletedText(deleted) : 'It was deleted, or its storage no longer holds it.'}
        </EmptyState>
      </section>
    )
  }

  return (
    <QueryState query={cronJob} label="Loading the recurring job" rows={3}>
      {(data) => (
        <>
          <StaleNotice what="this recurring job" onRefresh={() => void cronJob.refetch()} failed={cronJob.isError} />
          <CronPanel cronJob={data} fires={data.nextFires} page onDeleted={setDeleted} />
        </>
      )}
    </QueryState>
  )
}
