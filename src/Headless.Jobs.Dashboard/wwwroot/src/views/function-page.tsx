import { Link, useParams } from '@headless/dashboard-sdk/router'
import { QueryState, StaleNotice, useFunctions } from '../parts.tsx'
import { FunctionPanel } from './functions.tsx'

export default function FunctionPage() {
  const name = useParams().name ?? ''
  const functions = useFunctions()

  return (
    <QueryState query={functions} label="Loading the function" rows={4}>
      {(items) => {
        const item = items.find((candidate) => candidate.name === name)

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
      }}
    </QueryState>
  )
}
