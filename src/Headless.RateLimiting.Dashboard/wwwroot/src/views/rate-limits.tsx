import { PageFrame, PageHeader, usePageFrame } from '@headless/dashboard-sdk/components'

export default function RateLimitsView() {
  const frame = usePageFrame({ viewId: 'rate-limiting.rate-limits', rows: [] })

  return (
    <PageFrame
      frame={frame}
      header={<PageHeader title="Rate limits" description="Look up one subject's attempts. Counts can't be browsed." />}
    >
      {null}
    </PageFrame>
  )
}
