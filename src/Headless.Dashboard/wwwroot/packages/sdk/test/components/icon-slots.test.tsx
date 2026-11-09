import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { Button } from '../../src/components/button.tsx'
import { Card } from '../../src/components/display/card.tsx'
import { EmptyState } from '../../src/components/display/empty-state.tsx'
import { StatCard } from '../../src/components/display/stat-strip.tsx'
import { AttentionIcon, RefreshIcon, RetryIcon, RunsIcon } from '../../src/icons/index.ts'

describe('icon slots', () => {
  it('leads a button label with a hidden icon, so the label alone names the button', async () => {
    const onPress = vi.fn()
    render(
      <Button icon={RetryIcon} onPress={onPress}>
        Retry
      </Button>,
    )

    const button = screen.getByRole('button', { name: 'Retry' })
    const icon = button.querySelector('svg')!
    expect(icon.getAttribute('aria-hidden')).toBe('true')
    expect(button.textContent).toBe('Retry')

    await userEvent.click(button)
    expect(onPress).toHaveBeenCalledOnce()
  })

  it('names an icon-only button by its aria-label and keeps a disabled reason', () => {
    render(<Button icon={RefreshIcon} aria-label="Refresh" disabledReason="Needs operate permission" />)

    const button = screen.getByRole('button', { name: 'Refresh' })
    expect(button.querySelector('svg')).not.toBeNull()
    expect(button.getAttribute('aria-describedby')).toBe(screen.getByText('Needs operate permission').id)
  })

  it('puts the stat card icon beside the label without changing the link name or the state dot', () => {
    const router = createMemoryRouter([
      { path: '/', element: <StatCard label="Failed" value="23" tone="fail" icon={AttentionIcon} to="/jobs/runs?status=Failed" /> },
    ])
    render(<RouterProvider router={router} />)

    const link = screen.getByRole('link', { name: 'Failed 23' })
    expect(link.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    expect(link.querySelector('.hd-tone-fail .hd-mark')).not.toBeNull()
  })

  it('leads a card heading with its icon and shows the empty state with its own', () => {
    render(<Card title="Recent runs" icon={RunsIcon} empty="No run yet." emptyIcon={RunsIcon} />)

    const card = screen.getByRole('region', { name: 'Recent runs' })
    const heading = within(card).getByRole('heading', { name: 'Recent runs' })
    expect(heading.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    expect(within(card).getByText('No run yet.').parentElement?.querySelector('svg')).not.toBeNull()
  })

  it('shows an empty state without an icon as its text alone', () => {
    const { container } = render(<EmptyState>Nothing scheduled.</EmptyState>)

    expect(container.textContent).toBe('Nothing scheduled.')
    expect(container.querySelector('svg')).toBeNull()
  })
})
