'use no memo'

import type { ReactNode } from 'react'
import { AttentionIcon } from '../../icons/index.ts'
import { useStreamStatus } from '../../stream/hooks.ts'
import { Button } from '../button.tsx'
import { Announcer } from '../live.tsx'

/**
 * What a notice is about: `info` for a plain note, `warn` for data that may be out of date or incomplete, `error` for
 * a failed load. The tone tints the notice, so a failure never reads as a note.
 */
export type BannerTone = 'info' | 'warn' | 'error'

export interface BannerProps {
  children: ReactNode
  /** `info` by default. */
  tone?: BannerTone
  /** `status` for a notice read out politely, such as `Stale`; leave it out when a child carries its own role. */
  role?: 'status'
  /** `nav` for a row of page links; `div` by default. */
  as?: 'div' | 'nav'
  /** The accessible name, which a `nav` needs. */
  'aria-label'?: string
}

/** A one-line notice above a view's content, with its action at the end: stale data, a failed load, missing sources. */
export function Banner({ children, tone = 'info', role, as: Element = 'div', 'aria-label': label }: BannerProps) {
  return (
    <Element role={role} aria-label={label} className="hd-banner" data-tone={tone}>
      {children}
    </Element>
  )
}

export interface LoadErrorProps {
  /** What failed, as the error says it. */
  message: string
  onRetry: () => void
  /**
   * The view still shows its last good read under this notice. The notice then says so, and a failed refresh never
   * reads as a failed page.
   */
  kept?: boolean
}

/**
 * A failed load: the error and Retry, never an empty view. The icon and the tint carry the failure, so the message
 * stays in the text tone and is readable on the tint in both themes.
 */
export function LoadError({ message, onRetry, kept }: LoadErrorProps) {
  return (
    <Banner tone="error">
      <AttentionIcon className="text-destructive" />
      <span role="alert">{kept ? `${message} What is shown is from the last successful read.` : message}</span>
      <Button onPress={onRetry}>Retry</Button>
    </Banner>
  )
}

export interface StaleNoticeProps {
  /** Whether the data on screen may be out of date; the notice renders nothing otherwise. */
  stale: boolean
  /** What may be out of date, such as `these runs`. */
  what: string
  onRefresh: () => void
  /**
   * The view's own load failed. Its `LoadError` already says so and offers the same read again, so the stale notice
   * stays out of the way instead of stacking a second notice over it.
   */
  failed?: boolean
}

/**
 * `Stale` while live updates stopped, with Refresh; the data on screen is kept. The turn to stale is announced once,
 * from a region that is always in the page; when the whole stream dropped, the top bar's live state already said so,
 * and this notice stays quiet.
 */
export function StaleNotice({ stale, what, onRefresh, failed = false }: StaleNoticeProps) {
  const { status } = useStreamStatus()
  const text = `Stale. Live updates stopped, so ${what} may be out of date.`

  return (
    <>
      <Announcer message={stale && status !== 'stale' ? `Live updates stopped for ${what}.` : ''} />
      {stale && !failed ? (
        <Banner tone="warn">
          <AttentionIcon className="text-proc" />
          <span>{text}</span>
          <Button onPress={onRefresh}>Refresh</Button>
        </Banner>
      ) : null}
    </>
  )
}
