import { useEffect, useState, useSyncExternalStore } from 'react'
import { ProgressBar as AriaProgressBar } from 'react-aria-components'
import { useDashboard } from '../context.tsx'
import { useNewRows } from '../data/hooks.ts'
import { useStreamStatus } from '../stream/hooks.ts'
import { Button } from './button.tsx'

/** A visually hidden polite live region: what it holds is announced, never shown. */
export function Announcer({ message }: { message: string }) {
  return (
    <div role="status" aria-live="polite" className="hd-visually-hidden">
      {message}
    </div>
  )
}

function useSecondsSince(since: number | null) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (since === null) {
      return undefined
    }

    const timer = setInterval(() => setNow(Date.now()), 1_000)

    return () => clearInterval(timer)
  }, [since])

  return since === null ? 0 : Math.max(0, Math.round((now - since) / 1_000))
}

/**
 * The top bar's live state: `Live`, `Stale — 40 s` with `Reconnect`, or `Paused · 3 updates` while the tab holds its
 * updates (`paused` is how many wait); a dropped stream wins over Paused. Each turn to Stale or Paused is announced
 * once through a live region; the ticking seconds and the waiting count are not.
 */
export function LiveIndicator({ paused }: { paused?: number | null }) {
  const { stream } = useDashboard()
  const { status, staleSince } = useStreamStatus()
  const seconds = useSecondsSince(staleSince)
  const stale = status === 'stale'
  const held = stale ? null : paused
  // Only a turn away from live is news; the steady state is not announced.
  const announcement = stale ? 'Live updates stopped. What you see may be out of date.' : held != null ? 'Live updates paused in this tab.' : ''

  return (
    <span className={`hd-live${stale ? ' hd-live-stale' : held != null ? ' hd-live-paused' : ''}`}>
      <span className="hd-live-label">
        <i aria-hidden="true" />
        {stale
          ? `Stale — ${seconds} s`
          : held != null
            ? `Paused · ${held} ${held === 1 ? 'update' : 'updates'}`
            : status === 'live'
              ? 'Live'
              : 'Connecting'}
      </span>
      {stale ? (
        <Button variant="ghost" onPress={() => stream.reconnect()}>
          Reconnect
        </Button>
      ) : null}
      <Announcer message={announcement} />
    </span>
  )
}

/**
 * `3 new` above a live list when rows arrived that the list does not show yet: the list keeps the operator's place
 * until they choose to see them. The count is announced through a live region.
 */
export function NewRowsNotice({ topic }: { topic: string }) {
  const { count, show } = useNewRows(topic)

  return (
    <>
      {count > 0 ? (
        <Button variant="ghost" className="hd-new-rows" onPress={() => void show()}>
          {`${count} new`}
        </Button>
      ) : null}
      <Announcer message={count > 0 ? `${count} new ${count === 1 ? 'row' : 'rows'}` : ''} />
    </>
  )
}

/** Shows the toasts: outcomes with no place on screen, and Undo for an action that has an inverse. */
export function ToastRegion() {
  const { toasts } = useDashboard()
  const items = useSyncExternalStore(toasts.subscribe, toasts.getToasts)

  return (
    <section className="hd-toasts" aria-label="Notifications">
      {items.map((toast) => (
        <div key={toast.id} role="status" className="hd-toast">
          <span>{toast.message}</span>
          {toast.action ? (
            <Button
              variant="ghost"
              onPress={() => {
                toasts.dismiss(toast.id)
                toast.action?.onAction()
              }}
            >
              {toast.action.label}
            </Button>
          ) : null}
          <Button variant="ghost" className="hd-icon" aria-label="Dismiss" onPress={() => toasts.dismiss(toast.id)}>
            ×
          </Button>
        </div>
      ))}
    </section>
  )
}

/** A progress bar with its label and value text, such as an Operation's `1,200 of 5,000 rows`. */
export function ProgressBar({ label, value, max, valueText }: { label: string; value: number; max: number; valueText: string }) {
  const percent = max > 0 ? Math.min(100, (value / max) * 100) : 0

  return (
    <AriaProgressBar className="hd-progress" aria-label={label} value={value} maxValue={Math.max(max, 1)} valueLabel={valueText}>
      <span className="hd-progress-text">{valueText}</span>
      <span className="hd-bar" aria-hidden="true">
        <i style={{ inlineSize: `${percent}%` }} />
      </span>
    </AriaProgressBar>
  )
}
