import { render as renderElement, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactElement } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { formatJson, PayloadView } from '../../src/components/payload/payload-view.tsx'
import { LocaleProvider } from '../../src/locale/locale.tsx'

function render(element: ReactElement) {
  return renderElement(<LocaleProvider>{element}</LocaleProvider>)
}

const encoder = new TextEncoder()

function shownText() {
  return document.querySelector('pre')!.textContent ?? ''
}

function stubClipboard() {
  const writeText = vi.fn(async () => undefined)
  vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
  return writeText
}

describe('payload view', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('colours a large integer as a number and keeps every digit', () => {
    render(<PayloadView content='{"id":9007199254740993,"ok":true}' contentType="application/json" />)

    expect(shownText()).toBe('{\n  "id": 9007199254740993,\n  "ok": true\n}')
    const number = screen.getByText('9007199254740993')
    expect(number.tagName).toBe('SPAN')
    expect(number.className).toBe('text-syntax-number')
    expect(screen.getByText('"id"').className).toBe('text-syntax-key')
    expect(screen.getByText('true').className).toBe('text-syntax-literal')
  })

  it('copies the payload as the server sent it, not the formatted, coloured text', async () => {
    const user = userEvent.setup()
    const writeText = stubClipboard()
    render(<PayloadView content='{"id":9007199254740993, "name":"a"}' contentType="application/json" />)

    await user.click(screen.getByRole('button', { name: 'Copy' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith('{"id":9007199254740993, "name":"a"}')
  })

  it('shows malformed JSON as plain text, and still copies it', async () => {
    const user = userEvent.setup()
    const writeText = stubClipboard()
    const content = '{"id": 7, "name": "unterminated}'
    render(<PayloadView content={content} contentType="application/json" />)

    expect(shownText()).toBe(content)
    expect(document.querySelectorAll('pre span')).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: 'Copy' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith(content)
  })

  it('shows markup inside a payload as text, never as elements', () => {
    render(<PayloadView content='{"note":"<script>alert(1)</script><img src=x onerror=alert(2)>"}' contentType="application/json" />)

    const pre = document.querySelector('pre')!
    expect(pre.querySelector('script, img')).toBeNull()
    expect(pre.textContent).toContain('"<script>alert(1)</script><img src=x onerror=alert(2)>"')
    expect(screen.getByText('"<script>alert(1)</script><img src=x onerror=alert(2)>"').className).toBe(
      'text-syntax-string',
    )
  })

  it('shows a non-JSON payload with markup as plain text', () => {
    render(<PayloadView content="<script>alert(1)</script>" contentType="text/plain" />)

    expect(document.querySelector('pre script')).toBeNull()
    expect(shownText()).toBe('<script>alert(1)</script>')
  })

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
