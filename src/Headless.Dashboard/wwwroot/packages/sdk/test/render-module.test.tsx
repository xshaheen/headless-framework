import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { defineModule } from '../src/define-module.ts'
import { renderModule } from '../src/testing/render-module.tsx'

const diagnostics = defineModule({
  id: 'diag',
  product: 'platform',
  routes: [{ path: 'runs', lazy: () => import('./fixtures/runs-view.tsx') }],
})

const runs = {
  items: [
    { id: 'r1', version: '1', title: 'billing.charge #1', status: 'Queued' },
    { id: 'r2', version: '1', title: 'billing.charge #2', status: 'Queued' },
  ],
}

describe('renderModule', () => {
  it('mounts a module route, and a pushed change event updates its rendered row', async () => {
    const view = renderModule(diagnostics, { path: 'runs', api: () => runs })

    const row = await screen.findByRole('row', { name: /billing.charge #1/ })
    expect(within(row).getByText('Queued')).toBeTruthy()
    expect(view.stream.topics()).toEqual(['diag.run'])

    view.stream.push('change', {
      module: 'diag',
      kind: 'run',
      id: 'r1',
      topic: 'diag.run',
      version: '2',
      projection: { status: 'Failed' },
    })

    await waitFor(() => expect(within(screen.getByRole('row', { name: /billing.charge #1/ })).getByText('Failed')).toBeTruthy())
    expect(view.requests.filter((request) => request.path === 'diag/runs')).toHaveLength(1)
  })

  it('announces new rows through a live region instead of moving the list', async () => {
    const view = renderModule(diagnostics, { path: 'runs', api: () => runs })
    await screen.findByRole('row', { name: /billing.charge #1/ })

    view.stream.push('change', { module: 'diag', kind: 'run', id: 'r3', topic: 'diag.run', version: '1', projection: { status: 'Failed' } })

    expect(screen.getByRole('button', { name: '1 new' })).toBeTruthy()
    expect(screen.getAllByRole('status').some((region) => region.textContent === '1 new row')).toBe(true)
    expect(screen.queryByRole('row', { name: /r3/ })).toBeNull()
  })

  it('renders a viewer’s actions disabled with the reason', async () => {
    renderModule(diagnostics, { path: 'runs', api: () => runs, permissions: { canOperate: false } })
    const action = await screen.findByRole('button', { name: 'Requeue' })

    expect(action.getAttribute('aria-disabled')).toBe('true')
    expect(action.getAttribute('aria-describedby')).toBeTruthy()
    expect(document.getElementById(action.getAttribute('aria-describedby')!)?.textContent).toBe('Needs operate permission')
  })

  it('answers an unhandled API call with 404', async () => {
    renderModule(diagnostics, { path: 'runs' })

    expect((await screen.findByRole('alert')).textContent).toBe('No API handler for GET diag/runs.')
  })
})
