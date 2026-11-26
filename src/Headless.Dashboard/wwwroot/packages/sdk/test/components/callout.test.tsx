import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Callout } from '../../src/components/display/callout.tsx'
import { RunsIcon } from '../../src/icons/index.ts'
import { axeViolations } from '../../src/testing/axe.ts'

describe('Callout', () => {
  it('names its region by its heading and holds the paragraphs and lists it is given', async () => {
    const { container } = render(
      <Callout title="How it works" icon={RunsIcon}>
        <p>Each key is claimed once.</p>
        <ul>
          <li>A replay returns the stored answer.</li>
          <li>An expired key is claimed again.</li>
        </ul>
      </Callout>,
    )

    const region = screen.getByRole('region', { name: 'How it works' })
    expect(within(region).getByRole('heading', { name: 'How it works' })).toBeTruthy()
    expect(within(region).getByText('Each key is claimed once.')).toBeTruthy()
    expect(within(region).getAllByRole('listitem')).toHaveLength(2)
    expect(await axeViolations(container)).toEqual([])
  })

  it('is plain prose with no heading and no landmark without a title', async () => {
    const { container } = render(
      <Callout>
        <p>No release, expire, or delete here.</p>
      </Callout>,
    )

    expect(screen.queryByRole('heading')).toBeNull()
    expect(screen.queryByRole('region')).toBeNull()
    expect(screen.getByText('No release, expire, or delete here.')).toBeTruthy()
    expect(await axeViolations(container)).toEqual([])
  })
})
