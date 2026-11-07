import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Figure, type FigureProps } from '../../src/components/figure/figure.tsx'

afterEach(() => {
  vi.restoreAllMocks()
})

describe('Figure', () => {
  it('shows the value with its comparison', () => {
    render(<Figure label="Succeeded" value="98.2 %" comparison="−1.1 pt vs previous 24 h" />)

    expect(screen.getByRole('figure').textContent).toBe('Succeeded98.2 %−1.1 pt vs previous 24 h')
  })

  it('refuses a bare number in development', () => {
    // React reports the thrown render error to the console as well; the assertion is on the error itself.
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const bare = { label: 'Succeeded', value: '98.2 %' } as FigureProps

    expect(() => render(<Figure {...bare} />)).toThrow('The figure "Succeeded" has no comparison')
    expect(() => render(<Figure label="Succeeded" value="98.2 %" comparison="" />)).toThrow('A bare number is not a figure')
  })
})
