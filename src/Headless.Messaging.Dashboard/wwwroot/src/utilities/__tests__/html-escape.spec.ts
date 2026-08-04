import { describe, expect, it } from 'vitest'
import { escapeHtml } from '../html-escape'

describe('escapeHtml', () => {
  it('neutralizes tag delimiters so message content cannot become live DOM', () => {
    expect(escapeHtml('<img src=x onerror=alert(1)>')).toBe(
      '&lt;img src=x onerror=alert(1)&gt;',
    )
  })

  it('escapes ampersands before angle brackets so entities are not reconstructable', () => {
    // Escaping < and > first would turn "&lt;script&gt;" back into a live tag.
    expect(escapeHtml('&lt;script&gt;')).toBe('&amp;lt;script&amp;gt;')
  })

  it('leaves text without HTML-significant characters untouched', () => {
    expect(escapeHtml('plain message 42')).toBe('plain message 42')
  })

  it('returns an empty string unchanged', () => {
    expect(escapeHtml('')).toBe('')
  })
})
