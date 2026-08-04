import { describe, expect, it } from 'vitest'
import { formatJsonForDisplay } from '../json-format'

describe('formatJsonForDisplay', () => {
  it('pretty-prints valid JSON with two-space indentation', () => {
    expect(formatJsonForDisplay('{"a":1,"b":{"c":2}}')).toBe(
      '{\n  "a": 1,\n  "b": {\n    "c": 2\n  }\n}',
    )
  })

  it('returns undefined for empty, null and undefined input', () => {
    expect(formatJsonForDisplay('')).toBeUndefined()
    expect(formatJsonForDisplay(null)).toBeUndefined()
    expect(formatJsonForDisplay(undefined)).toBeUndefined()
  })

  it('returns undefined for unparseable input', () => {
    expect(formatJsonForDisplay('{ not json')).toBeUndefined()
  })

  it('emits no HTML markup, so the result is safe for text interpolation', () => {
    const formatted = formatJsonForDisplay('{"a":1,"b":2}')

    // The pre-fix implementation replaced newlines with <br> and spaces with &nbsp;,
    // which forced callers onto v-html and turned payload values into live DOM.
    expect(formatted).not.toContain('<br>')
    expect(formatted).not.toContain('&nbsp;')
    expect(formatted).toContain('\n')
  })

  it('leaves markup inside payload values verbatim for the template to escape', () => {
    const formatted = formatJsonForDisplay('{"x":"<img src=x onerror=alert(1)>"}')

    // Escaping belongs to Vue's text interpolation; double-escaping here would show
    // operators &lt;img&gt; instead of the value actually stored on the job.
    expect(formatted).toBe('{\n  "x": "<img src=x onerror=alert(1)>"\n}')
  })
})
