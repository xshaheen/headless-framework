import { fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { defineModule } from '../../src/define-module.ts'
import { SandboxedHtmlFrame, type PreviewWidth } from '../../src/preview/index.ts'
import { renderModule } from '../../src/testing/render-module.tsx'

function previewModule(defaultWidth?: PreviewWidth) {
  function EmailView() {
    return (
      <SandboxedHtmlFrame
        path="inbox/emails/42/html"
        query={{ part: 'body' }}
        title="Reset your password"
        defaultWidth={defaultWidth}
      />
    )
  }

  return defineModule({
    id: 'inbox',
    product: 'dev-inbox',
    routes: [{ path: 'emails', lazy: async () => ({ default: EmailView }) }],
  })
}

describe('SandboxedHtmlFrame', () => {
  it('loads the module endpoint in a fully sandboxed frame that sends no referrer, behind a skeleton until it loads', async () => {
    renderModule(previewModule(), { path: 'emails', basePath: '/dash' })

    const frame = await screen.findByTitle('Reset your password')
    expect(frame.tagName).toBe('IFRAME')
    // Present and empty: every sandbox restriction applies, which an absent attribute would lift.
    expect(frame.getAttribute('sandbox')).toBe('')
    expect(frame.getAttribute('referrerpolicy')).toBe('no-referrer')
    expect(frame.getAttribute('src')).toBe('/dash/api/inbox/emails/42/html?part=body')
    expect(screen.getByRole('status', { name: 'Loading Reset your password' })).toBeTruthy()

    fireEvent.load(frame)

    expect(screen.queryByRole('status', { name: 'Loading Reset your password' })).toBeNull()
    expect(screen.getByTitle('Reset your password')).toBe(frame)
  })

  it('opens at Desktop, narrows to a phone on Phone, and can open at Phone', async () => {
    const user = userEvent.setup()
    const view = renderModule(previewModule(), { path: 'emails' })

    const desktop = await screen.findByRole('button', { name: 'Desktop' })
    const phone = screen.getByRole('button', { name: 'Phone' })
    const box = screen.getByTitle('Reset your password').parentElement!
    expect(desktop.getAttribute('aria-pressed')).toBe('true')
    // jsdom lays nothing out, so the test reads the width rule the built stylesheet applies.
    expect(box.classList.contains('w-full')).toBe(true)

    await user.click(phone)

    expect(phone.getAttribute('aria-pressed')).toBe('true')
    expect(box.classList.contains('w-[375px]')).toBe(true)
    view.unmount()

    renderModule(previewModule('phone'), { path: 'emails' })
    expect((await screen.findByRole('button', { name: 'Phone' })).getAttribute('aria-pressed')).toBe('true')
  })
})
