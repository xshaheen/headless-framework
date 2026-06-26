import { describe, it, expect } from 'vitest'
import { describeCron, hasSecondsSegment, isValidCronExpression } from '@/utilities/cron'

describe('hasSecondsSegment', () => {
  it('accepts a 6-segment (seconds) expression', () => {
    expect(hasSecondsSegment('0 0 12 * * *')).toBe(true)
  })

  it('trims surrounding whitespace before counting segments', () => {
    expect(hasSecondsSegment('   0 0 12 * * *   ')).toBe(true)
  })

  it('rejects a 5-segment (no seconds) expression', () => {
    expect(hasSecondsSegment('0 12 * * *')).toBe(false)
  })

  it('rejects empty / nullish input', () => {
    expect(hasSecondsSegment('')).toBe(false)
    expect(hasSecondsSegment(null)).toBe(false)
    expect(hasSecondsSegment(undefined)).toBe(false)
  })
})

describe('isValidCronExpression', () => {
  it('is true for a parseable 6-segment expression', () => {
    expect(isValidCronExpression('0 0 12 * * *')).toBe(true)
    expect(isValidCronExpression('*/5 * * * * *')).toBe(true)
  })

  it('enforces the seconds rule: a parseable 5-segment expression is still invalid', () => {
    expect(isValidCronExpression('0 12 * * *')).toBe(false)
  })

  it('is false for a 6-token but unparseable expression', () => {
    expect(isValidCronExpression('foo bar baz qux quux corge')).toBe(false)
  })
})

describe('describeCron', () => {
  // Behavioral canary: a cronstrue bump that breaks seconds handling fails here.
  it('renders the seconds frequency in human form', () => {
    expect(describeCron('*/5 * * * * *').toLowerCase()).toContain('second')
  })

  it('produces a non-empty description for a valid expression', () => {
    expect(describeCron('0 0 12 * * *')).not.toBe('')
  })

  it('returns the supplied fallback for an invalid expression', () => {
    expect(describeCron('foo bar baz qux quux corge', 'Invalid cron expression')).toBe(
      'Invalid cron expression',
    )
  })

  it('defaults the fallback to an empty string', () => {
    expect(describeCron('')).toBe('')
    expect(describeCron(null)).toBe('')
  })
})
