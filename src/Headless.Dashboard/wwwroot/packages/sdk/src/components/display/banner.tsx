'use no memo'

import type { ReactNode } from 'react'
import { Button } from '../button.tsx'
import { Text } from './text.tsx'

export interface BannerProps {
  children: ReactNode
  /** `status` for a notice read out politely, such as `Stale`; leave it out when a child carries its own role. */
  role?: 'status'
  /** `nav` for a row of page links; `div` by default. */
  as?: 'div' | 'nav'
  /** The accessible name, which a `nav` needs. */
  'aria-label'?: string
}

/** A one-line notice above a view's content, with its action at the end: stale data, a failed load, missing sources. */
export function Banner({ children, role, as: Element = 'div', 'aria-label': label }: BannerProps) {
  return (
    <Element role={role} aria-label={label} className="hd-banner">
      {children}
    </Element>
  )
}

export interface LoadErrorProps {
  /** What failed, as the error says it. */
  message: string
  onRetry: () => void
}

/** A failed load: the error and Retry, never an empty view. */
export function LoadError({ message, onRetry }: LoadErrorProps) {
  return (
    <Banner>
      <Text tone="error" role="alert">
        {message}
      </Text>
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
}

/** `Stale` while live updates stopped, with Refresh; the data on screen is kept. */
export function StaleNotice({ stale, what, onRefresh }: StaleNoticeProps) {
  if (!stale) {
    return null
  }

  return (
    <Banner role="status">
      <span>{`Stale. Live updates stopped, so ${what} may be out of date.`}</span>
      <Button variant="ghost" onPress={onRefresh}>
        Refresh
      </Button>
    </Banner>
  )
}
