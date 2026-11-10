import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RelativeTime } from '../../src/components/display/relative-time.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'

describe('RelativeTime', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-10T12:00:00Z'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('turns a next fire into due once its time passes, with no new data', () => {
    render(
      <LocaleProvider>
        <RelativeTime value="2026-11-10T12:00:03Z" scheduled />
      </LocaleProvider>,
    )

    expect(screen.getByText(/^in 3 sec\.$/)).toBeTruthy()

    act(() => {
      vi.advanceTimersByTime(5_000)
    })

    expect(screen.getByText(/^due 2 sec\. ago$/)).toBeTruthy()
  })

  it('counts a past time up without saying due when it is not scheduled', () => {
    render(
      <LocaleProvider>
        <RelativeTime value="2026-11-10T11:59:58Z" />
      </LocaleProvider>,
    )

    act(() => {
      vi.advanceTimersByTime(3_000)
    })

    expect(screen.getByText(/^5 sec\. ago$/)).toBeTruthy()
  })
})
