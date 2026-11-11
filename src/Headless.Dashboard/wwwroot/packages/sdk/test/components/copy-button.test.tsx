import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CopyButton } from '../../src/components/copy-button.tsx'

afterEach(() => {
  vi.unstubAllGlobals()
  Reflect.deleteProperty(document, 'execCommand')
})

function stubClipboard(writeText: (text: string) => Promise<void>) {
  vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
}

describe('CopyButton', () => {
  it('copies the text and announces it, from the keyboard alone', async () => {
    const user = userEvent.setup()
    const writeText = vi.fn(async () => undefined)
    stubClipboard(writeText)
    render(<CopyButton text="01a1cab0" aria-label="Copy id 01a1cab0" tooltip="Copy id" />)

    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Copy id 01a1cab0' }))
    await user.keyboard('{Enter}')

    expect(writeText).toHaveBeenCalledWith('01a1cab0')
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Copied'))
  })

  it('keeps its accessible name while its visible label reads Copied', async () => {
    const user = userEvent.setup()
    stubClipboard(async () => undefined)
    render(<CopyButton text="{}" label="Copy all" />)

    await user.click(screen.getByRole('button', { name: 'Copy all' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Copy all' }).textContent).toBe('Copied'))
  })

  it('falls back to the copy command when the browser refuses the clipboard', async () => {
    const user = userEvent.setup()
    stubClipboard(async () => {
      throw new DOMException('Write permission denied.', 'NotAllowedError')
    })
    let selected = ''
    const execCommand = vi.fn(() => {
      selected = (document.activeElement as HTMLTextAreaElement).value
      return true
    })
    Object.defineProperty(document, 'execCommand', { configurable: true, value: execCommand })
    render(<CopyButton text="stack line" label="Copy" />)

    const button = screen.getByRole('button', { name: 'Copy' })
    await user.click(button)

    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Copied'))
    expect(execCommand).toHaveBeenCalledWith('copy')
    expect(selected).toBe('stack line')
    // The off-screen field is gone and focus is back on the button.
    expect(document.querySelector('textarea')).toBeNull()
    expect(document.activeElement).toBe(button)
  })

  it('says so when no way to copy is left', async () => {
    const user = userEvent.setup()
    vi.stubGlobal('navigator', { ...navigator, clipboard: undefined })
    Object.defineProperty(document, 'execCommand', { configurable: true, value: () => false })
    render(<CopyButton text="x" label="Copy" />)

    await user.click(screen.getByRole('button', { name: 'Copy' }))

    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Copy failed. Select the text to copy it.'))
  })
})
