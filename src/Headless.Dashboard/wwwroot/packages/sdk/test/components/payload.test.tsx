import { render as renderElement, screen } from '@testing-library/react'
import type { ReactElement } from 'react'
import { describe, expect, it } from 'vitest'
import { formatJson, PayloadView } from '../../src/components/payload/payload-view.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'

function render(element: ReactElement) {
  return renderElement(<LocaleProvider>{element}</LocaleProvider>)
}

const encoder = new TextEncoder()

function shownText() {
  return document.querySelector('pre')!.textContent ?? ''
}

describe('payload view', () => {
  it('formats JSON and keeps a large integer exact', () => {
    expect(formatJson('{"id":9007199254740993,"tags":[],"a":{"b":"x, y"}}')).toBe(
      '{\n  "id": 9007199254740993,\n  "tags": [],\n  "a": {\n    "b": "x, y"\n  }\n}',
    )

    render(<PayloadView content='{"amount":12345678901234567890}' contentType="application/json" />)
    expect(screen.getByText(/12345678901234567890/)).toBeTruthy()
  })

  it('shows the size and the first 64 KB of a large payload, with Copy all', () => {
    render(<PayloadView content={'x'.repeat(70 * 1024)} contentType="text/plain" />)

    expect(screen.getByText('70.0 KB, first 64 KB shown')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Copy all' })).toBeTruthy()
    expect(shownText()).toBe('x'.repeat(64 * 1024))
  })

  it('cuts a 70 KB Arabic payload below 64 KB of UTF-8 bytes without splitting a character', () => {
    // Each Arabic letter is two UTF-8 bytes; a leading `x` puts every letter on an odd byte offset, so a cut at
    // exactly 65,536 bytes would land in the middle of one.
    const content = `x${'مرحبا'.repeat(7 * 1024)}`
    expect(encoder.encode(content).length).toBe(1 + 70 * 1024)

    render(<PayloadView content={content} contentType="text/plain" />)

    const shown = shownText()
    expect(screen.getByText('70.0 KB, first 64 KB shown')).toBeTruthy()
    expect(encoder.encode(shown).length).toBe(65_535)
    expect(content.startsWith(shown)).toBe(true)
    expect(shown).not.toContain('�')
  })

  it('shows a payload of exactly 64 KB whole', () => {
    const content = 'م'.repeat(32 * 1024)
    expect(encoder.encode(content).length).toBe(65_536)

    render(<PayloadView content={content} contentType="text/plain" />)

    expect(shownText()).toBe(content)
    expect(screen.getByText('64.0 KB')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Copy' })).toBeTruthy()
  })

  it('names an unsupported contract version instead of failing to parse', () => {
    render(<PayloadView content="{}" unsupportedVersion={3} />)

    expect(screen.getByText('Unsupported contract version 3')).toBeTruthy()
  })
})
