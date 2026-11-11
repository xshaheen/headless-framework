import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ActionConfirm, type ActionConfirmProps } from '../../src/commands/action-confirm.tsx'
import { Dialog } from '../../src/components/dialog.tsx'
import { Kbd } from '../../src/components/marks.tsx'

function confirm(props: Partial<ActionConfirmProps> = {}) {
  return (
    <ActionConfirm
      isOpen
      onCancel={vi.fn()}
      onConfirm={vi.fn()}
      title="Delete 3 runs"
      reach="12 stored occurrences go with it"
      confirmLabel="Delete 3 runs"
      cancelLabel="Keep runs"
      confirmation="confirm"
      {...props}
    />
  )
}

describe('ActionConfirm', () => {
  it('is an alert dialog that reads what the action reaches and starts focus on backing out', async () => {
    render(confirm())

    const dialog = screen.getByRole('alertdialog', { name: 'Delete 3 runs' })
    const described = document.getElementById(dialog.getAttribute('aria-describedby')!)
    expect(described?.textContent).toBe('12 stored occurrences go with it')
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Keep runs' })))
  })

  it('backs out on Enter pressed on open, and never runs the action', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    const onConfirm = vi.fn()
    render(confirm({ onCancel, onConfirm }))
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Keep runs' })))

    await user.keyboard('{Enter}')

    expect(onCancel).toHaveBeenCalledOnce()
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('stays open on Escape while the call is in flight', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(confirm({ onCancel, isBusy: true }))

    await user.keyboard('{Escape}')

    expect(onCancel).not.toHaveBeenCalled()
    expect(screen.getByRole('alertdialog')).toBeTruthy()
  })
})

describe('Dialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('places itself from where the visible part of the screen starts, following it as a phone keyboard scrolls the page', () => {
    const viewport = Object.assign(new EventTarget(), { offsetTop: 0, height: 400, width: 400 })
    vi.stubGlobal('visualViewport', viewport)

    render(
      <Dialog isOpen onOpenChange={vi.fn()} title="Requeue run">
        Body
      </Dialog>,
    )

    screen.getByRole('dialog')
    const scrim = document.querySelector<HTMLElement>('[style*="--visual-viewport-height"]')!
    expect(scrim.style.getPropertyValue('--visual-viewport-top')).toBe('0px')
    expect(scrim.style.getPropertyValue('--visual-viewport-height')).not.toBe('')

    act(() => {
      viewport.offsetTop = 120
      viewport.dispatchEvent(new Event('scroll'))
    })

    expect(scrim.style.getPropertyValue('--visual-viewport-top')).toBe('120px')
  })
})

describe('Kbd', () => {
  it('reads a chord left to right on a right-to-left page', () => {
    render(
      <div dir="rtl">
        <Kbd>⌘K</Kbd>
      </div>,
    )

    expect(screen.getByText('⌘K').getAttribute('dir')).toBe('ltr')
  })
})
