// The React Compiler's memo caches cost the SDK more bytes than these renders save, and the shell's assets sit near
// their size budget.
'use no memo'

import { Link, useNavigate } from '../router/index.ts'
import { Button } from './button.tsx'
import { useViewKeys } from './keymap.tsx'

/** The longest correlation id the framework writes, and so the longest one the trace can find. */
export const maxCorrelationIdLength = 200

/**
 * The shell path of the trace of `correlationId`: `/trace#<id>`. The id rides in the fragment, which a browser never
 * sends to the server, so it stays out of request logs while the link stays shareable.
 */
export function traceAddress(correlationId: string) {
  return `/trace#${encodeURIComponent(correlationId)}`
}

/** Why `correlationId` cannot be traced, or `undefined` when it can. */
export function traceRefusal(correlationId: string | null | undefined) {
  return !correlationId
    ? 'No correlation id'
    : correlationId.length > maxCorrelationIdLength
      ? `Id longer than ${maxCorrelationIdLength} characters`
      : undefined
}

/** `Open trace` for an object's correlation id: a link to its trace, or a disabled button that says why there is none. */
export function OpenTrace({ correlationId }: { correlationId: string | null | undefined }) {
  const refusal = traceRefusal(correlationId)

  return refusal || !correlationId ? (
    <Button variant="ghost" disabledReason={refusal}>
      Open trace
    </Button>
  ) : (
    <Link to={traceAddress(correlationId)}>Open trace</Link>
  )
}

/**
 * `T` opens the trace of the open object's correlation id while the calling view is mounted. An id that cannot be
 * traced, or one not read yet, still takes the key, so it never falls through to a view mounted earlier.
 */
export function useTraceKey(correlationId: string | null | undefined) {
  const navigate = useNavigate()

  useViewKeys({
    trace: () => {
      if (correlationId && !traceRefusal(correlationId)) {
        void navigate(traceAddress(correlationId))
      }
    },
  })
}
