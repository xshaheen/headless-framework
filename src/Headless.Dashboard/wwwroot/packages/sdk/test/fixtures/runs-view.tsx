import { useState } from 'react'
import type { CommandDefinition } from '../../src/commands/api.ts'
import { CommandAction } from '../../src/commands/command-action.tsx'
import { useOperations } from '../../src/commands/operations.tsx'
import { Button } from '../../src/components/button.tsx'
import { NewRowsNotice } from '../../src/components/live.tsx'
import { StatusMark } from '../../src/components/marks.tsx'
import { DataTable } from '../../src/components/table/data-table.tsx'
import { useDashboard, useModule } from '../../src/context.tsx'
import { useDashboardList } from '../../src/data/hooks.ts'

export interface Run {
  id: string
  version?: string
  title: string
  status: string
}

export const requeue: CommandDefinition = {
  id: 'requeue',
  label: 'Requeue',
  objectKind: 'run',
  permission: 'operate',
  confirmation: 'confirm',
  typedConfirmationAbove: 100,
  requiresReason: true,
  supportsFilter: true,
  arguments: { type: 'object', required: [], properties: {} },
  canRun: true,
}

/** A view as a module author writes one: a live list, its new rows notice, an action per row, and an Operation. */
export default function RunsView() {
  const { api } = useDashboard()
  const module = useModule()
  const { start } = useOperations()
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const runs = useDashboardList<Run>({
    topic: 'diag.run',
    fetch: () => api.request({ method: 'GET', path: 'diag/runs' }),
  })
  const command = module.canOperate ? requeue : { ...requeue, canRun: false, disabledReason: 'Needs operate permission' }

  if (runs.isPending) {
    return <p>Loading runs</p>
  }

  if (runs.isError) {
    return <p role="alert">{runs.error.message}</p>
  }

  return (
    <section aria-label="Runs">
      <NewRowsNotice topic="diag.run" />
      <Button onPress={() => start({ module: 'diag', command, filter: { status: 'failed' }, noun: 'failed runs' })}>
        Requeue all failed
      </Button>
      <CommandAction module="diag" command={command} ids={[...selected]} noun="runs" />
      <DataTable
        label="Runs"
        rows={runs.data.items}
        selectedIds={selected}
        onSelectionChange={setSelected}
        emptyState="No runs yet."
        columns={[
          { id: 'title', header: 'Run', cell: (run) => run.title, isRowHeader: true },
          { id: 'status', header: 'Status', cell: (run) => <StatusMark tone={run.status === 'Failed' ? 'needs' : 'active'} label={run.status} /> },
        ]}
      />
    </section>
  )
}
