import { useDelayed } from '@headless/dashboard-sdk/components'
import { Link, useParams } from '@headless/dashboard-sdk/router'
import { LoadError, Skeleton, StaleNotice } from '../parts.tsx'
import { FunctionPanel, useFunctions } from './functions.tsx'

/** One function's own page: the function panel's overview, runs, and actions, detached from its list. */
export default function FunctionPage() {
  const name = useParams().name ?? ''
  const functions = useFunctions()
  const showLoading = useDelayed(functions.isPending)
  const item = functions.data?.find((candidate) => candidate.name === name)

  if (functions.isPending) {
    return showLoading ? <Skeleton label="Loading the function" rows={4} /> : null
  }

  if (functions.isError) {
    return <LoadError message={functions.error.message} onRetry={() => void functions.refetch()} />
  }

  if (!item) {
    return (
      <section aria-label="Function not found">
        <p role="alert">{`No live node registers the function ${name}.`}</p>
        <Link to="/jobs/functions">Back to functions</Link>
      </section>
    )
  }

  return (
    <>
      <StaleNotice what="this function" onRefresh={() => void functions.refetch()} />
      <FunctionPanel item={item} page />
    </>
  )
}
