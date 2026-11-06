import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { CronPanel, useCronJob } from '../cron-parts.tsx'
import { QueryState, StaleNotice } from '../parts.tsx'

export default function CronJobPage() {
  const cronJobId = useParams().cronJobId ?? ''
  const cronJob = useCronJob(cronJobId)

  if (cronJob.error instanceof DashboardApiError && cronJob.error.status === 404) {
    return (
      <section aria-label="Recurring job not found">
        <p role="alert">This recurring job no longer exists.</p>
        <Link to="/jobs/cron-jobs">Back to recurring jobs</Link>
      </section>
    )
  }

  return (
    <QueryState query={cronJob} label="Loading the recurring job" rows={3}>
      {(data) => (
        <>
          <StaleNotice what="this recurring job" onRefresh={() => void cronJob.refetch()} />
          <CronPanel cronJob={data} fires={data.nextFires} page />
        </>
      )}
    </QueryState>
  )
}
