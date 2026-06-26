import { describe, it, expect } from 'vitest'
import { parseJsonSafe } from '@/utilities/jsonBig'

describe('parseJsonSafe', () => {
  // The reason json-bigint exists: int64 message ids must survive parsing intact.
  it('preserves int64 ids beyond Number.MAX_SAFE_INTEGER as exact strings', () => {
    const bigId = '9223372036854775807' // long.MaxValue
    const { parsed, isJson } = parseJsonSafe(`{"messageId": ${bigId}}`)

    expect(isJson).toBe(true)
    const obj = parsed as Record<string, unknown>
    expect(obj.messageId).toBe(bigId)
    expect(typeof obj.messageId).toBe('string')
  })

  it('guards precisely the case native JSON.parse loses', () => {
    const bigId = '9223372036854775807'
    // Sanity contrast: native parse silently corrupts the value to a double.
    expect(String(JSON.parse(`{"id": ${bigId}}`).id)).not.toBe(bigId)
  })

  it('parses ordinary JSON objects (small numbers stay numbers)', () => {
    const { parsed, isJson } = parseJsonSafe('{"a":1,"b":"x"}')

    expect(isJson).toBe(true)
    expect(parsed).toMatchObject({ a: 1, b: 'x' })
  })

  it('reports invalid JSON without throwing', () => {
    expect(parseJsonSafe('not json at all')).toEqual({ parsed: null, isJson: false })
  })

  it('treats a bare JSON string literal as valid JSON', () => {
    const { parsed, isJson } = parseJsonSafe('"hello"')

    expect(isJson).toBe(true)
    expect(parsed).toBe('hello')
  })
})
