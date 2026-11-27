import { PageFrame, PageHeader, usePageFrame } from '@headless/dashboard-sdk/components'

export default function ClusterView() {
  const frame = usePageFrame({ viewId: 'cluster.nodes', rows: [] })

  return (
    <PageFrame frame={frame} header={<PageHeader title="Nodes" />}>
      {null}
    </PageFrame>
  )
}
