import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Tag } from '../../src/components/display/tag.tsx'

describe('Tag', () => {
  it('draws the DEV badge on the proc fill in mono, without the outline tags’ muted text', () => {
    render(<Tag variant="dev">DEV</Tag>)

    const badge = screen.getByText('DEV')
    // jsdom applies no stylesheet, so the test reads the classes the built stylesheet styles.
    expect(['bg-proc', 'text-on-proc', 'type-badge'].filter((rule) => badge.classList.contains(rule))).toEqual([
      'bg-proc',
      'text-on-proc',
      'type-badge',
    ])
    expect(badge.classList.contains('text-muted-foreground')).toBe(false)
  })
})
