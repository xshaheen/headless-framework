import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { KeyValueTable } from '../../src/components/display/key-value-table.tsx'

describe('KeyValueTable', () => {
  it('applies horizontal page-gutter padding only when placement is page', () => {
    const { rerender } = render(<KeyValueTable label="Headers" rows={[{ name: 'host', value: 'node-a' }]} />)
    const panelWrap = screen.getByRole('table', { name: 'Headers' }).parentElement!
    expect(panelWrap.className).not.toContain('px-4')

    rerender(<KeyValueTable label="Headers" placement="page" rows={[{ name: 'host', value: 'node-a' }]} />)
    const pageWrap = screen.getByRole('table', { name: 'Headers' }).parentElement!
    expect(pageWrap.className).toContain('px-4')
  })
})
