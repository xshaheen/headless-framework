import { describe, it, expect } from 'vitest'
import { formatDateTime, timeAgo } from '@/utilities/dateTimeParser'

describe('formatDateTime', () => {
  it('returns an empty string for nullish input', () => {
    expect(formatDateTime('')).toBe('')
    expect(formatDateTime(null)).toBe('')
    expect(formatDateTime(undefined)).toBe('')
  })

  it('produces a dd.MM.yyyy HH:mm:ss shape', () => {
    expect(formatDateTime('2024-01-02T03:04:05Z')).toMatch(
      /^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$/,
    )
  })

  it('normalizes a Z-less, space-separated timestamp to the same instant as its ISO form', () => {
    // Time-zone independent: both inputs denote the same UTC instant, so they must render identically.
    expect(formatDateTime('2024-01-02 03:04:05')).toBe(formatDateTime('2024-01-02T03:04:05Z'))
  })

  it('preserves ISO timestamps that already contain a UTC offset', () => {
    expect(formatDateTime('2026-07-28T22:33:24.320959+00:00')).toBe(
      formatDateTime('2026-07-28T22:33:24.320959Z'),
    )
    expect(formatDateTime('2026-07-29T01:33:24.320959+03:00')).toBe(
      formatDateTime('2026-07-28T22:33:24.320959Z'),
    )
    expect(formatDateTime('2026-07-28T19:33:24.320959-03:00')).toBe(
      formatDateTime('2026-07-28T22:33:24.320959Z'),
    )
    expect(formatDateTime('2026-07-29T01:33:24.320959+0300')).toBe(
      formatDateTime('2026-07-28T22:33:24.320959Z'),
    )
  })

  it('returns an empty string for an invalid timestamp', () => {
    expect(formatDateTime('not-a-date')).toBe('')
  })
})

describe('timeAgo', () => {
  it('returns an empty string for nullish input', () => {
    expect(timeAgo('')).toBe('')
  })

  // Behavioral canary: a timeago.js bump that breaks relative formatting fails here.
  it('describes a past instant relative to now', () => {
    expect(timeAgo('2000-01-01T00:00:00Z').toLowerCase()).toContain('ago')
  })

  it('preserves timestamps that already contain a UTC offset', () => {
    expect(timeAgo('2000-01-01T03:00:00+03:00')).toBe(timeAgo('2000-01-01T00:00:00Z'))
    expect(timeAgo('1999-12-31T21:00:00-03:00')).toBe(timeAgo('2000-01-01T00:00:00Z'))
  })

  it('returns an empty string for an invalid timestamp', () => {
    expect(timeAgo('not-a-date')).toBe('')
  })
})
