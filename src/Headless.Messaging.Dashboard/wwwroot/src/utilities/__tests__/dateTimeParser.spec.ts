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
})

describe('timeAgo', () => {
  it('returns an empty string for nullish input', () => {
    expect(timeAgo('')).toBe('')
  })

  // Behavioral canary: a timeago.js bump that breaks relative formatting fails here.
  it('describes a past instant relative to now', () => {
    expect(timeAgo('2000-01-01T00:00:00Z').toLowerCase()).toContain('ago')
  })
})
