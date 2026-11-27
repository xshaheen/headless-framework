import { describe, expect, it } from 'vitest'
import {
  isRecord,
  readBoolean,
  readList,
  readNumber,
  readOneOf,
  readOptionalString,
  readRecord,
  readString,
  shapeError,
} from '../src/data/shape.ts'

describe('data shape readers', () => {
  it('accepts plain objects and rejects primitives, null, and arrays', () => {
    expect(isRecord({ a: 1 })).toBe(true)
    expect(isRecord(null)).toBe(false)
    expect(isRecord([])).toBe(false)
    expect(isRecord('x')).toBe(false)
    expect(readRecord({ ok: true }, 'payload')).toEqual({ ok: true })
    expect(() => readRecord([], 'payload')).toThrow(shapeError('payload').message)
  })

  it('narrows strings, optional strings, finite numbers, booleans, lists, and enums', () => {
    expect(readString('abc', 'name')).toBe('abc')
    expect(() => readString(42, 'name')).toThrow('unexpected name')

    expect(readOptionalString('abc', 'note')).toBe('abc')
    expect(readOptionalString(null, 'note')).toBeNull()
    expect(readOptionalString(undefined, 'note')).toBeNull()
    expect(() => readOptionalString(123, 'note')).toThrow('unexpected note')

    expect(readNumber(0, 'count')).toBe(0)
    expect(() => readNumber(Number.NaN, 'count')).toThrow('unexpected count')
    expect(() => readNumber(Number.POSITIVE_INFINITY, 'count')).toThrow('unexpected count')
    expect(() => readNumber('1', 'count')).toThrow('unexpected count')

    expect(readBoolean(false, 'flag')).toBe(false)
    expect(() => readBoolean('false', 'flag')).toThrow('unexpected flag')

    expect(readList(['a', 'b'], 'items', (item) => readString(item, 'item'))).toEqual(['a', 'b'])
    expect(() => readList('not-a-list', 'items', (item) => readString(item, 'item'))).toThrow('unexpected items')

    const states = ['Alive', 'Dead'] as const
    expect(readOneOf(states, 'Alive', 'state')).toBe('Alive')
    expect(() => readOneOf(states, 'Unknown', 'state')).toThrow('unexpected state')
  })
})
