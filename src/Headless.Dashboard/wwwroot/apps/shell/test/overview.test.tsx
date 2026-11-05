import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { Figure, ModuleSection } from '../src/shell-data.ts'
import { moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

describe('Overview', () => {
  it('refetches its figures on attention.changed while no module entry is loaded', async () => {
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    let failed = 4
    const figures = (): ModuleSection<Figure>[] => [
      { id: 'diag', displayName: 'Diagnostics', stale: false, items: [{ id: 'failed', label: 'Failed runs', value: failed }] },
    ]
    const shell = renderShell(shellState(modules), {
      files: moduleFiles(modules),
      api: (request) => ({ modules: request.path === 'overview' ? figures() : [] }),
    })

    expect(await screen.findByText('4')).toBeTruthy()

    failed = 6
    shell.stream.push('attention.changed', { module: 'diag' })

    expect(await screen.findByText('6')).toBeTruthy()
    await waitFor(() => expect(shell.requests.filter((request) => request.path === 'overview')).toHaveLength(2))
    expect(shell.imported).toEqual([])
  })
})
