import { useDelayed } from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { CronPanel, useCronJob } from '../cron-parts.tsx'
import { LoadError, Skeleton, StaleNotice } from '../parts.tsx'

/** One cron job's own page: the cron job panel's schedule, occurrences, and actions, detached from its list. */
export default function CronJobPage() {
  const cronJobId = useParams().cronJobId ?? ''
  const cronJob = useCronJob(cronJobId)
  const showLoading = useDelayed(cronJob.isPending)

  if (cronJob.isPending) {
    return showLoading ? <Skeleton label="Loading the recurring job" rows={3} /> : null
  }

  if (cronJob.isError) {
    if (cronJob.error instanceof DashboardApiError && cronJob.error.status === 404) {
      return (
        <section aria-label="Recurring job not found">
          <p role="alert">This recurring job no longer exists.</p>
          <Link to="/jobs/cron-jobs">Back to recurring jobs</Link>
        </section>
      )
    }

    return <LoadError message={cronJob.error.message} onRetry={() => void cronJob.refetch()} />
  }

  return (
    <>
      <StaleNotice what="this recurring job" onRefresh={() => void cronJob.refetch()} />
      <CronPanel cronJob={cronJob.data} fires={cronJob.data.nextFires} page />
    </>
  )
}
