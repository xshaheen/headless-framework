import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { AttentionItem, ModuleSection } from '../src/shell-data.ts'
import { moduleFiles, moduleManifest, shellState } from './fixtures/modules.tsx'
import { renderShell } from './harness.tsx'

function item(id: string, groupKey: string, title: string, count: number): AttentionItem {
  return { id, groupKey, title, severity: 'critical', count, capped: false, target: { module: 'diag', kind: 'run', id }, since: '2026-11-05T09:14:00Z' }
}

describe('Needs you', () => {
  it('groups items by module and cause, and shows Stale for a module the server marks stale', async () => {
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' }), moduleManifest('slow', { displayName: 'Slow module' })]
    const sections: ModuleSection<AttentionItem>[] = [
      {
        id: 'diag',
        displayName: 'Diagnostics',
        stale: false,
        items: [
          item('r1', 'timeout', '23 failed runs: TimeoutException', 23),
          item('r2', 'card', '4 failed runs: CardException', 4),
        ],
      },
      { id: 'slow', displayName: 'Slow module', stale: true, items: [] },
    ]

    renderShell(shellState(modules), {
      path: '/needs-you',
      api: (request) => ({ modules: request.path === 'needs-you' ? sections : [] }),
    })

    const diag = await screen.findByRole('region', { name: 'Diagnostics' })
    expect(within(diag).getAllByRole('list')).toHaveLength(2)
    expect(within(diag).getByRole('list', { name: '23 failed runs: TimeoutException' })).toBeTruthy()

    const slow = screen.getByRole('region', { name: 'Slow module' })
    expect(within(slow).getByText('Stale')).toBeTruthy()
    expect(within(slow).getByText(/did not answer in time/)).toBeTruthy()

    // The rail counts every item.
    expect(within(screen.getByRole('navigation', { name: 'Dashboard' })).getByRole('link', { name: /Needs you/ }).textContent).toContain('27')
  })

  it('refetches on attention.changed while no module entry is loaded', async () => {
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]
    let current: AttentionItem[] = []
    const shell = renderShell(shellState(modules), {
      path: '/needs-you',
      files: moduleFiles(modules),
      api: (request) =>
        request.path === 'needs-you' ? { modules: [{ id: 'diag', displayName: 'Diagnostics', stale: false, items: current }] } : { modules: [] },
    })

    expect(await screen.findByText('Nothing needs you here.')).toBeTruthy()

    current = [item('r9', 'timeout', '1 failed run: TimeoutException', 1)]
    shell.stream.push('attention.changed', { module: 'diag' })

    expect(await screen.findByText('1 failed run: TimeoutException')).toBeTruthy()
    expect(shell.imported).toEqual([])
  })

  it('opens the selected item in the inspector with its related objects and Open trace', async () => {
    const user = userEvent.setup()
    const modules = [moduleManifest('diag', { displayName: 'Diagnostics' })]

    renderShell(shellState(modules), {
      path: '/needs-you',
      api: (request) => {
        if (request.path === 'related') {
          return {
            groups: [
              {
                label: 'Published messages',
                ids: ['msg'],
                items: [{ target: { module: 'diag', kind: 'run', id: 'm1' }, title: 'orders.placed', traceUrl: 'https://apm.example/trace/abc' }],
                hasMore: false,
              },
            ],
            stale: [],
          }
        }

        return { modules: request.path === 'needs-you' ? [{ id: 'diag', displayName: 'Diagnostics', stale: false, items: [item('r1', 'k', 'Run failed', 1)] }] : [] }
      },
    })

    await user.click(await screen.findByRole('button', { name: /Run failed/ }))

    const inspector = await screen.findByRole('complementary', { name: 'Needs you: Run failed' })
    expect(await within(inspector).findByRole('link', { name: 'Open trace' })).toBeTruthy()

    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('complementary', { name: 'Needs you: Run failed' })).toBeNull())
  })
})
