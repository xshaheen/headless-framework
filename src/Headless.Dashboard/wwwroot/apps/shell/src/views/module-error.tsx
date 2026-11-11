// The React Compiler's memo caches cost this view more bytes than its rare renders save, and the shell's assets
// sit at their size budget.
'use no memo'

import { Button } from '@headless/dashboard-sdk/components'
import { Link, isRouteErrorResponse, useRouteError } from '@headless/dashboard-sdk/router'
import type { BootstrapModule } from '../bootstrap.ts'
import { ChunkLoadError } from '../module-loader.ts'

/** Why a refused module did not load, naming both SDK versions so the operator knows which side to update. */
export function refusalText(module: BootstrapModule) {
  const refusal = module.refusal
  const builtAgainst = refusal?.builtAgainst ? `SDK ${refusal.builtAgainst}` : 'an unknown SDK version'

  return `${module.displayName} did not load: it was built against ${builtAgainst}, and this dashboard runs SDK ${refusal?.shellSdkVersion ?? 'unknown'}. Update the module or the dashboard so their SDK versions match.`
}

/** A view-sized card on the canvas, for a view the shell draws itself. */
const card = 'flex flex-col gap-3 rounded-2xl border bg-card p-7 shadow-2xl'
/** Running text in a card, kept to a readable measure. */
const prose = 'max-w-[72ch] type-body'

export function ModuleError({ module }: { module: BootstrapModule }) {
  return (
    <section className={`${card} max-w-(--dialog-width)`} aria-labelledby="module-error-title">
      <h2 id="module-error-title" className="hd-title">
        {module.displayName} is not available
      </h2>
      <p role="alert" className={prose}>
        {refusalText(module)}
      </p>
      <p className="hd-hint">
        The rest of the dashboard keeps working. <Link to="/">Go to the dashboard</Link>
      </p>
    </section>
  )
}

export function NotFound() {
  return (
    <section className={`${card} m-auto max-w-[46ch] items-center text-center`} aria-labelledby="not-found-title">
      <h2 id="not-found-title" className="hd-title">
        Nothing lives at this address
      </h2>
      <p className={prose}>The link may name a module this dashboard does not serve, or one you may not view.</p>
      <Link to="/">Go to the dashboard</Link>
    </section>
  )
}

/**
 * A view that failed to load or render. A file missing during a rolling deploy reads `Dashboard updated`, because a
 * reload fixes it; any other failure says the view failed and offers the same way out.
 */
export function RouteError() {
  const error = useRouteError()

  if (error instanceof ChunkLoadError) {
    return <UpdatedNotice />
  }

  const message = isRouteErrorResponse(error)
    ? `The dashboard answered ${error.status}.`
    : error instanceof Error && error.message
      ? error.message
      : 'This view failed to load.'

  return (
    <section className={`${card} max-w-(--dialog-width)`} role="alert" aria-labelledby="route-error-title">
      <h2 id="route-error-title" className="hd-title">
        This view failed
      </h2>
      <p className={prose}>{message} Reload the page, or open another view.</p>
      <div className="hd-actions">
        <Button variant="primary" onPress={() => window.location.reload()}>
          Reload
        </Button>
        <Link to="/">Go to the dashboard</Link>
      </div>
    </section>
  )
}

/**
 * `Dashboard updated — Reload`: the page names files from an earlier deploy. Above the view the frame sets its margin;
 * in place of a failed view it sits on the canvas as it is.
 */
export function UpdatedNotice({ className }: { className?: string }) {
  return (
    <div className={`hd-banner ${className ?? ''}`} role="alert">
      <span>Dashboard updated — reload to load the new version.</span>
      <Button variant="primary" onPress={() => window.location.reload()}>
        Reload
      </Button>
    </div>
  )
}
