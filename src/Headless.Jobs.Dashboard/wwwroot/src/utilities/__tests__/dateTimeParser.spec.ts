import { describe, it, expect } from 'vitest'
import { formatDate, formatTime } from '@/utilities/dateTimeParser'

describe('formatTime', () => {
  it('renders sub-second values as milliseconds', () => {
    expect(formatTime(250, true)).toBe('250ms')
  })

  it('rolls up >= 1000ms into seconds', () => {
    expect(formatTime(1000, true)).toBe('1s')
  })

  it('renders bare seconds', () => {
    expect(formatTime(45)).toBe('45s')
  })

  it('renders minutes (with and without remainder seconds)', () => {
    expect(formatTime(90)).toBe('1m 30s')
    expect(formatTime(120)).toBe('2m')
  })

  it('renders hours (with and without remainder minutes)', () => {
    expect(formatTime(3661)).toBe('1h 1m')
    expect(formatTime(7200)).toBe('2h')
  })

  it('renders days and hours', () => {
    expect(formatTime(90000)).toBe('1d 1h')
  })
})

describe('formatDate', () => {
  it('formats a UTC instant in the requested time zone deterministically', () => {
    expect(formatDate('2024-01-02T03:04:05Z', true, 'UTC')).toBe('02.01.2024 03:04:05')
  })

  it('omits the time component when includeTime is false', () => {
    expect(formatDate('2024-01-02T03:04:05Z', false, 'UTC')).toBe('02.01.2024')
  })

  it('normalizes a space-separated, Z-less timestamp as UTC', () => {
    expect(formatDate('2024-01-02 03:04:05', true, 'UTC')).toBe('02.01.2024 03:04:05')
  })

  it('returns an empty string for empty input', () => {
    expect(formatDate('')).toBe('')
  })
})
