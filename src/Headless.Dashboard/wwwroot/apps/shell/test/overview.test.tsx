import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { Figure, ModuleSection, OverviewSection } from '../src/shell-data.ts'
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

  it('shows a figure whose earlier period has nothing to compare with the reason in place of a change', async () => {
    const modules = [moduleManifest('jobs', { displayName: 'Jobs' })]
    const sections: ModuleSection<Figure>[] = [
      {
        id: 'jobs',
        displayName: 'Jobs',
        stale: false,
        items: [
          { id: 'share', label: 'Runs that succeeded, last 24 h', value: 11.7, unit: '%', comparison: { label: 'no runs finished in the previous 24 h', value: null } },
        ],
      },
    ]
    renderShell(shellState(modules), { api: (request) => ({ modules: request.path === 'overview' ? sections : [] }) })

    const label = await screen.findByText((_, element) => element?.className === 'hd-figure-label')
    expect(label.textContent).toBe('Runs that succeeded, last 24 h, no runs finished in the previous 24 h')
  })

  it('draws one strip track per module track, past runs filled and next fires hollow', async () => {
    const now = Date.now()
    const modules = [moduleManifest('jobs', { displayName: 'Jobs' })]
    const sections: OverviewSection[] = [
      {
        id: 'jobs',
        displayName: 'Jobs',
        stale: false,
        items: [],
        tracks: [
          {
            id: 'reports.nightly',
            label: 'reports.nightly',
            ticks: [
              { id: 'r1', at: new Date(now - 20 * 60_000).toISOString(), label: 'failed', tone: 'fail', target: { module: 'jobs', kind: 'run', id: 'r1' } },
              { id: 'fire:c1:1', at: new Date(now + 20 * 60_000).toISOString(), label: 'next fire', target: null },
            ],
          },
        ],
      },
    ]
    renderShell(shellState(modules), { api: (request) => ({ modules: request.path === 'overview' ? sections : [] }) })

    const strip = await screen.findByRole('grid', { name: 'Schedules' })
    expect(within(strip).getByRole('rowheader').textContent).toBe('reports.nightly')
    const ticks = within(strip).getAllByRole('gridcell')
    expect(ticks.map((tick) => tick.getAttribute('aria-label')?.split(',')[1]?.trim())).toEqual(['failed', 'next fire'])
    expect(ticks[1]!.getAttribute('class')).toContain('hd-strip-future')
  })
})
