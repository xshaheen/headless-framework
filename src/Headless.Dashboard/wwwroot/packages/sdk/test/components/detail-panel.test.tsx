import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { DetailPanel } from '../../src/components/frame/detail-panel.tsx'
import { PageFrame, usePageFrame } from '../../src/components/frame/page-frame.tsx'

const rows = [{ id: 'r1' }]

function Panel() {
  const frame = usePageFrame({ viewId: 'jobs.failed', rows })

  return (
    <PageFrame
      frame={frame}
      panel={
        <DetailPanel
          label="Run details"
          name="billing.charge"
          id="r1"
          status={{ tone: 'fail', label: 'Failed' }}
          tabs={[
            { id: 'overview', label: 'Overview', content: <p>Overview body</p> },
            { id: 'arguments', label: 'Arguments', content: <p>Arguments body</p> },
            { id: 'chain', label: 'Chain', content: <p>Chain body</p> },
          ]}
        />
      }
    >
      <p>List</p>
    </PageFrame>
  )
}

function renderPanel() {
  render(<RouterProvider router={createMemoryRouter([{ path: '/', element: <Panel /> }])} />)
}

describe('DetailPanel', () => {
  it('names the state, the object, and its id with a copy control', () => {
    renderPanel()

    const panel = screen.getByRole('region', { name: 'Run details' })
    expect(screen.getByRole('heading', { level: 2, name: 'billing.charge' })).toBeTruthy()
    expect(panel.textContent).toContain('Failed')
    expect(screen.getByRole('button', { name: 'Copy id r1' })).toBeTruthy()
    // No page route was given, so the panel offers no detach.
    expect(screen.queryByRole('link', { name: 'Open in its own page' })).toBeNull()
  })

  it('moves between tabs with roving focus, and each tab controls the panel it shows', async () => {
    const user = userEvent.setup()
    renderPanel()

    const overview = screen.getByRole('tab', { name: 'Overview' })
    expect(overview.getAttribute('aria-selected')).toBe('true')
    expect(screen.getAllByRole('tab').map((tab) => tab.tabIndex)).toEqual([0, -1, -1])
    expect(screen.getByRole('tabpanel', { name: 'Overview' }).id).toBe(overview.getAttribute('aria-controls'))

    overview.focus()
    await user.keyboard('{ArrowRight}')

    const args = screen.getByRole('tab', { name: 'Arguments' })
    expect(document.activeElement).toBe(args)
    expect(args.getAttribute('aria-selected')).toBe('true')
    expect(screen.getByRole('tabpanel', { name: 'Arguments' }).textContent).toBe('Arguments body')

    await user.keyboard('{End}')
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Chain' }))

    await user.keyboard('{ArrowRight}')
    expect(document.activeElement).toBe(overview)
    expect(screen.getByText('Overview body')).toBeTruthy()

    await user.keyboard('{ArrowLeft}')
    expect(screen.getByText('Chain body')).toBeTruthy()
  })

  it('renders as the object’s own page outside a PageFrame, with nothing to close or move', () => {
    render(<DetailPanel label="Run details" name="billing.charge" />)

    expect(screen.getByRole('region', { name: 'Run details' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Close details' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Move details/ })).toBeNull()
  })
})
