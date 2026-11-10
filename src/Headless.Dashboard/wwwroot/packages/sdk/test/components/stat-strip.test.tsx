import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { StatCard, StatStrip } from '../../src/components/display/stat-strip.tsx'

describe('StatStrip', () => {
  it('shows each card’s label, value, and state dot, and draws no empty chart frame', () => {
    render(
      <StatStrip label="Runs by state">
        <StatCard label="Failed" value="23" tone="fail" />
        <StatCard label="Succeeded" value="1,204" tone="ok" caption="all tenants" />
      </StatStrip>,
    )

    const strip = screen.getByRole('region', { name: 'Runs by state' })
    expect(strip.textContent).toBe('Failed 23Succeeded 1,204 all tenants')
    expect(strip.querySelector('.hd-tone-fail .hd-mark')).not.toBeNull()
    expect(strip.querySelector('.hd-tone-ok .hd-mark')).not.toBeNull()
    expect(strip.querySelector('svg')).toBeNull()
    expect(within(strip).queryByRole('img')).toBeNull()
  })

  it('links a card to the list it counts', () => {
    const router = createMemoryRouter([{ path: '/', element: <StatCard label="Failed" value="23" tone="fail" to="/jobs/runs?status=Failed" /> }])
    render(<RouterProvider router={router} />)

    expect(screen.getByRole('link', { name: 'Failed 23' }).getAttribute('href')).toBe('/jobs/runs?status=Failed')
  })
})
