import { StaleNotice } from '../../src/components/display/banner.tsx'
import { useModule } from '../../src/context.tsx'
import { useModuleStale, useNodeStale } from '../../src/stream/stale.ts'

function NodeNotice({ node }: { node: string }) {
  const module = useModule()

  return <StaleNotice stale={useNodeStale(module.id, node)} what={`${node}'s runs`} onRefresh={() => undefined} />
}

export default function StaleView() {
  const module = useModule()
  const { isStale, unavailable } = useModuleStale(module.id)

  return (
    <main>
      <StaleNotice stale={isStale} what="these runs" onRefresh={() => undefined} />
      {unavailable.length > 0 && <p>{`Unavailable: ${unavailable.join(', ')}`}</p>}
      <NodeNotice node="node-a" />
      <NodeNotice node="node-b" />
    </main>
  )
}
