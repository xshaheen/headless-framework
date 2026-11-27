import { act, screen, waitFor, within } from '@testing-library/react'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { described, email, emailRow, now, open, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('emails view', () => {
  it('lists emails with subject, recipient, sender, and the attachment count of the one that has one', async () => {
    open(
      {
        emails: [
          email({ subject: 'Welcome aboard' }),
          email({
            subject: 'Your invoice',
            senderName: 'billing',
            attachments: [
              { index: 0, fileName: 'a.pdf', contentType: 'application/pdf', sizeBytes: 10, isOmitted: false },
            ],
          }),
        ],
      },
      { path: 'emails' },
    )

    const invoice = await screen.findByRole('row', { name: /Your invoice/ })
    expect(within(invoice).getByText('ada@example.com')).toBeTruthy()
    expect(within(invoice).getByText('billing')).toBeTruthy()
    expect(within(invoice).getByText('1')).toBeTruthy()
    expect(screen.getByText(/Development only/)).toBeTruthy()
  })

  it('sends the search to the emails list and shows the matching rows', async () => {
    const actor = user()
    const view = open(
      { emails: [email({ subject: 'Welcome aboard' }), email({ subject: 'Your invoice' })] },
      { path: 'emails' },
    )

    await screen.findByRole('row', { name: /Welcome aboard/ })
    await actor.type(screen.getByRole('searchbox', { name: 'Search emails' }), 'invoice')

    await waitFor(() => expect(screen.queryByRole('row', { name: /Welcome aboard/ })).toBeNull())
    expect(screen.getByRole('row', { name: /Your invoice/ })).toBeTruthy()
    expect(
      view.requests.some((request) => request.path === 'dev-inbox/emails' && request.query?.search === 'invoice'),
    ).toBe(true)
  })

  it('mounts the preview frame on the email’s html address only for an email that has an HTML body', async () => {
    const actor = user()
    const html = email({ subject: 'Styled', hasHtml: true })
    const plain = email({ subject: 'Plain' })
    open({ emails: [html, plain] }, { path: 'emails' })

    await actor.click(await screen.findByRole('row', { name: /Styled/ }))
    const frame = await screen.findByTitle('Styled')
    expect(frame.getAttribute('src')).toContain(`dev-inbox/emails/${html.id}/html`)

    await actor.click(screen.getByRole('row', { name: /Plain/ }))
    await screen.findByRole('region', { name: `Email ${plain.id}` })
    expect(screen.queryByRole('tab', { name: 'HTML' })).toBeNull()
    expect(screen.queryByTitle('Plain')).toBeNull()
  })

  it('mounts no frame before the email’s detail answers', async () => {
    const html = email({ subject: 'Styled', hasHtml: true })
    let answer: (value: unknown) => void = () => {}
    const pending = new Promise((resolve) => {
      answer = resolve
    })
    const api = (request: { path: string }) =>
      request.path === `dev-inbox/emails/${html.id}`
        ? pending
        : request.path === 'dev-inbox/channels'
          ? { channels: [{ kind: 'email', capacity: 500 }] }
          : request.path === 'dev-inbox/emails'
            ? { items: [emailRow(html)], nextCursor: null }
            : { commands: [] }
    const { renderModule } = await import('@headless/dashboard-sdk/testing')
    const inbox = (await import('../src/module.ts')).default
    renderModule(inbox, { api, path: `emails?open=${html.id}` })

    await screen.findByRole('region', { name: `email ${html.id}` })
    expect(screen.queryByTitle('Styled')).toBeNull()
    answer(html)
    expect(await screen.findByTitle('Styled')).toBeTruthy()
  })

  it('links a kept attachment’s download and says an omitted one’s content was omitted', async () => {
    const actor = user()
    const message = email({
      attachments: [
        { index: 0, fileName: 'invoice.pdf', contentType: 'application/pdf', sizeBytes: 2048, isOmitted: false },
        { index: 1, fileName: 'scan.tiff', contentType: 'image/tiff', sizeBytes: 900_000, isOmitted: true },
      ],
    })
    open({ emails: [message] }, { path: `emails?open=${message.id}` })

    await actor.click(await screen.findByRole('tab', { name: 'Attachments' }))

    const link = await screen.findByRole('link', { name: 'Download invoice.pdf' })
    expect(link.getAttribute('href')).toContain(`dev-inbox/emails/${message.id}/attachments/0`)
    expect(screen.getByText('Content omitted (over the capture limit)')).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Download scan.tiff' })).toBeNull()
  })

  it('offers the .eml download of the open email', async () => {
    const message = email()
    open({ emails: [message] }, { path: `emails?open=${message.id}` })

    const link = await screen.findByRole('link', { name: 'Download .eml' })
    expect(link.getAttribute('href')).toContain(`dev-inbox/emails/${message.id}/eml`)
  })

  it('shows a failed email’s error', async () => {
    const failed = email({
      failed: true,
      textBody: null,
      failureError: 'At least one of MessageHtml or MessageText must be provided.',
    })
    open({ emails: [failed] }, { path: `emails?open=${failed.id}` })

    expect(await screen.findByText(/The send failed: At least one of MessageHtml/)).toBeTruthy()
  })

  it('deletes after a confirm naming the email and reads the list again', async () => {
    const actor = user()
    const message = email({ subject: 'Reset your password' })
    const view = open({ emails: [message] }, { path: `emails?open=${message.id}` })

    await actor.click(await screen.findByRole('button', { name: 'Delete' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Reset your password')
    const lists = view.requests.filter((request) => request.path === 'dev-inbox/emails').length
    await actor.click(within(dialog).getByRole('button', { name: /Delete/ }))

    await waitFor(() =>
      expect(
        view.requests.some(
          (request) => request.method === 'POST' && request.path === 'dev-inbox/commands/dev-inbox.email.delete',
        ),
      ).toBe(true),
    )
    await waitFor(() =>
      expect(view.requests.filter((request) => request.path === 'dev-inbox/emails').length).toBeGreaterThan(lists),
    )
  })

  it('disables Delete and Clear for a viewer with the operate reason', async () => {
    const message = email()
    open({ emails: [message] }, { path: `emails?open=${message.id}`, permissions: { canOperate: false } })

    const remove = await screen.findByRole('button', { name: 'Delete' })
    const clear = screen.getByRole('button', { name: 'Clear' })
    expect(described(remove)).toContain('Needs operate permission')
    expect(described(clear)).toContain('Needs operate permission')
  })

  it('shows a new email from the stream through the new-rows control without reading the list again', async () => {
    const view = open({ emails: [email({ subject: 'First' })] }, { path: 'emails' })
    await screen.findByRole('row', { name: /First/ })
    const lists = view.requests.filter((request) => request.path === 'dev-inbox/emails').length
    const arrived = email({ subject: 'Arrived' })

    act(() =>
      view.stream.push('change', {
        module: 'dev-inbox',
        kind: 'email',
        id: arrived.id,
        topic: 'dev-inbox.email',
        version: String(arrived.sequence),
        projection: emailRow(arrived),
      }),
    )
    expect(await screen.findByRole('button', { name: '1 new' })).toBeTruthy()
    expect(view.requests.filter((request) => request.path === 'dev-inbox/emails').length).toBe(lists)
  })

  it('says an email that is gone no longer exists', async () => {
    open({ emails: [] }, { path: 'emails?open=00000000-0000-0000-0000-999999999999' })

    const gone = await screen.findByRole('alert')
    expect(gone.textContent).toContain('This email no longer exists.')
  })

  it('shows the open email as gone once a removal arrives from the stream', async () => {
    const message = email({ subject: 'Short lived' })
    const fake = { emails: [message] }
    const view = open(fake, { path: `emails?open=${message.id}` })
    await screen.findByRole('button', { name: 'Delete' })

    fake.emails.length = 0
    act(() =>
      view.stream.push('change', {
        module: 'dev-inbox',
        kind: 'email',
        id: message.id,
        topic: 'dev-inbox.email',
        version: String(message.sequence + 1),
        projection: null,
      }),
    )

    expect((await screen.findByRole('alert')).textContent).toContain('This email no longer exists.')
    await waitFor(() => expect(screen.queryByRole('row', { name: /Short lived/ })).toBeNull())
  })

  it('names the missing provider call when the host captures no email', async () => {
    open({ channels: ['sms', 'push'] }, { path: 'emails' })

    expect(await screen.findByText('This host captures no emails.')).toBeTruthy()
    expect(screen.getByText(/UseDevelopment\(\) in AddHeadlessEmails/)).toBeTruthy()
  })
})
