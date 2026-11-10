import { act, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { defineModule } from '../src/define-module.ts'
import { renderModule } from '../src/testing/render-module.tsx'

const diagnostics = defineModule({
  id: 'diag',
  product: 'platform',
  routes: [
    { path: '', lazy: async () => ({ default: () => <p>Home</p> }) },
    { path: 'health', lazy: () => import('./fixtures/stale-view.tsx') },
  ],
})

const moduleNotice = 'Stale. Live updates stopped, so these runs may be out of date.'

function nodeNotice(node: string) {
  return `Stale. Live updates stopped, so ${node}'s runs may be out of date.`
}

async function openHealth(view: ReturnType<typeof renderModule>) {
  await act(() => view.navigate('health'))
  await screen.findByRole('main')
}

describe('stale state', () => {
  it('reads a module stale on a view mounted after the report', async () => {
    const view = renderModule(diagnostics)
    await screen.findByText('Home')

    view.stream.push('module.stale', { module: 'diag', stale: true })
    await openHealth(view)

    expect(screen.getByText(moduleNotice)).toBeTruthy()
  })

  it('clears a module on stale: false and keeps the sources it could not reach', async () => {
    const view = renderModule(diagnostics, { path: 'health' })
    await screen.findByRole('main')

    view.stream.push('module.stale', { module: 'diag', stale: true })
    expect(screen.getByText(moduleNotice)).toBeTruthy()

    view.stream.push('module.stale', { module: 'diag', stale: false, unavailable: ['archive'] })
    expect(screen.queryByText(moduleNotice)).toBeNull()
    expect(screen.getByText('Unavailable: archive')).toBeTruthy()
  })

  it('ignores another module’s report', async () => {
    const view = renderModule(diagnostics)
    await screen.findByText('Home')

    view.stream.push('module.stale', { module: 'other', stale: true })
    view.stream.push('node.stale', { module: 'other', node: 'node-a', stale: true })
    await openHealth(view)

    expect(screen.queryByText(moduleNotice)).toBeNull()
    expect(screen.queryByText(nodeNotice('node-a'))).toBeNull()
  })

  it('keeps node staleness per node, and clears it on stale: false', async () => {
    const view = renderModule(diagnostics)
    await screen.findByText('Home')

    view.stream.push('node.stale', { module: 'diag', node: 'node-b', stale: true })
    await openHealth(view)

    expect(screen.getByText(nodeNotice('node-b'))).toBeTruthy()
    expect(screen.queryByText(nodeNotice('node-a'))).toBeNull()
    expect(screen.queryByText(moduleNotice)).toBeNull()

    view.stream.push('node.reset', { module: 'diag', node: 'node-b' })
    expect(screen.getByText(nodeNotice('node-b'))).toBeTruthy()

    view.stream.push('node.stale', { module: 'diag', node: 'node-b', stale: false })
    expect(screen.queryByText(nodeNotice('node-b'))).toBeNull()
  })

  it('starts the next page from no reports', async () => {
    const first = renderModule(diagnostics, { path: 'health' })
    await screen.findByRole('main')
    first.stream.push('module.stale', { module: 'diag', stale: true })
    first.unmount()

    renderModule(diagnostics, { path: 'health' })
    await screen.findByRole('main')

    expect(screen.queryByText(moduleNotice)).toBeNull()
  })
})
