import { PageFrame, PageHeader, usePageFrame } from '@headless/dashboard-sdk/components'

export default function HealthView() {
  const frame = usePageFrame({ viewId: 'cluster.health', rows: [] })

  return (
    <PageFrame frame={frame} header={<PageHeader title="Health checks" />}>
      {null}
    </PageFrame>
  )
}
