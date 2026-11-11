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

  it('shows a tab only when it has content, and opens the default tab first', async () => {
    const user = userEvent.setup()
    render(
      <DetailPanel
        label="Run details"
        name="billing.charge"
        defaultTab="exception"
        tabs={[
          { id: 'overview', label: 'Overview', content: <p>Overview body</p> },
          { id: 'exception', label: 'Exception', content: <p>Exception body</p> },
          { id: 'arguments', label: 'Arguments', content: null },
          { id: 'chain', label: 'Chain', content: false },
        ]}
      />,
    )

    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Overview', 'Exception'])
    expect(screen.getByRole('tab', { name: 'Exception', selected: true })).toBeTruthy()
    expect(screen.getByRole('tabpanel', { name: 'Exception' }).textContent).toBe('Exception body')

    // A picked tab wins over the default from then on.
    await user.click(screen.getByRole('tab', { name: 'Overview' }))
    expect(screen.getByRole('tabpanel', { name: 'Overview' }).textContent).toBe('Overview body')
  })

  it('falls back to the first tab when the default tab has no content', () => {
    render(
      <DetailPanel
        label="Run details"
        name="billing.charge"
        defaultTab="exception"
        tabs={[
          { id: 'overview', label: 'Overview', content: <p>Overview body</p> },
          { id: 'exception', label: 'Exception', content: null },
        ]}
      />,
    )

    expect(screen.getByRole('tab', { name: 'Overview', selected: true })).toBeTruthy()
  })

  it('names its window controls and shows each name in a tooltip on focus', async () => {
    const user = userEvent.setup()
    renderPanel()

    await user.click(screen.getByRole('button', { name: 'Copy id r1' }))
    // Keyboard focus, as an operator tabbing from the copy control past the tabs; a programmatic focus opens none.
    await user.tab()
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Move details to the side' }))
    expect((await screen.findByRole('tooltip')).textContent).toBe('Move details to the side')

    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close details' }))
    expect((await screen.findByRole('tooltip')).textContent).toBe('Close details')
  })

  it('renders as the object’s own page outside a PageFrame, with nothing to close or move', () => {
    render(<DetailPanel label="Run details" name="billing.charge" />)

    expect(screen.getByRole('region', { name: 'Run details' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Close details' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Move details/ })).toBeNull()
  })
})
