import { act, render, screen } from '@testing-library/react'
import { LocaleProvider } from '@headless/dashboard-sdk/locale'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Time } from '../src/parts.tsx'

describe('Time', () => {
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
        <Time value="2026-11-10T12:00:03Z" scheduled />
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
        <Time value="2026-11-10T11:59:58Z" />
      </LocaleProvider>,
    )

    act(() => {
      vi.advanceTimersByTime(3_000)
    })

    expect(screen.getByText(/^5 sec\. ago$/)).toBeTruthy()
  })
})
