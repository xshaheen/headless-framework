import { Button, SegmentedControl, Text, Timestamp } from '@headless/dashboard-sdk/components'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { RefreshIcon } from '@headless/dashboard-sdk/icons'
import { useNavigate } from '@headless/dashboard-sdk/router'
import type { ReactNode } from 'react'
import type { LeasesSummary } from './api.ts'

const leaseTypes = [
  { value: 'fenced', label: 'Fenced leases' },
  { value: 'locks', label: 'Distributed locks' },
] as const
type LeaseType = (typeof leaseTypes)[number]['value']

export function CellText({ title, children }: { title?: string; children: ReactNode }) {
  return <span title={title}>{children}</span>
}

/**
 * Switches between the module's two views. A host that serves one half has nothing to switch to, so it shows nothing
 * until the summary says both halves are served.
 */
export function LeaseTypeSwitch({ current, summary }: { current: LeaseType; summary: LeasesSummary | undefined }) {
  const navigate = useNavigate()

  if (!summary?.fencing || !summary.locks) {
    return null
  }

  return (
    <SegmentedControl
      label="Lease type"
      options={leaseTypes}
      value={current}
      onChange={(value) => void navigate(value === 'locks' ? '/leases/locks' : '/leases')}
    />
  )
}

/**
 * When the view last read the store, and a Refresh that reads it again. The views do not poll: a timer would repeat a
 * table-wide count every few seconds per open tab.
 */
export function ReadAt({
  at,
  isRefreshing,
  onRefresh,
}: {
  at: string | undefined
  isRefreshing: boolean
  onRefresh: () => void
}) {
  return (
    <>
      {at ? (
        <Text tone="hint">
          Read <Timestamp value={at} />
        </Text>
      ) : null}
      <Button icon={RefreshIcon} onPress={onRefresh} isDisabled={isRefreshing}>
        Refresh
      </Button>
    </>
  )
}

export function isNotFound(error: unknown) {
  return error instanceof DashboardApiError && error.status === 404
}
