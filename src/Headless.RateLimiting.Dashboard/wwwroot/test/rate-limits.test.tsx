import { act, screen, within } from '@testing-library/react'
import { DashboardApiError } from '@headless/dashboard-sdk/data'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { catalogue, described, fingerprint, held, now, open, subject, user, window } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
})

async function lookUp(actor: ReturnType<typeof user>, text = subject) {
  await actor.type(await screen.findByRole('textbox', { name: /^Subject/ }), text)
  await actor.click(screen.getByRole('button', { name: 'Look up' }))
}

function resetButton() {
  return screen.getByRole('button', { name: 'Reset this window' })
}

async function confirmReset(actor: ReturnType<typeof user>) {
  await actor.click(await screen.findByRole('button', { name: 'Reset this window' }))
  await actor.type(screen.getByRole('textbox', { name: /^Reason/ }), 'Verified by phone')
  await actor.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Reset this window' }))
}

async function chooseCustom(actor: ReturnType<typeof user>, limit: string, window: string) {
  await actor.selectOptions(await screen.findByRole('combobox', { name: /^Purpose/ }), 'Custom purpose')
  await actor.type(screen.getByRole('textbox', { name: /^Custom purpose/ }), 'export.request')

  if (limit) {
    await actor.type(screen.getByRole('textbox', { name: /^Limit/ }), limit)
  }

  if (window) {
    await actor.type(screen.getByRole('textbox', { name: /^Window$/ }), window)
  }
}

function lookups(requests: { path: string }[]) {
  return requests.filter((request) => request.path === 'rate-limiting/lookup').length
}

describe('rate limits', () => {
  it('lists the declared purposes with their limit and window, and states only the served figures', async () => {
    open()

    const table = await screen.findByRole('grid', { name: 'Purposes the app declares' })
    expect(
      within(table)
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.textContent),
    ).toEqual(['otp.verify310 min', 'password-reset.request31 h', 'sign-in.password515 min'])
    const stats = screen.getByRole('region', { name: 'Rate limits' })
    expect(within(stats).getByText('Purposes')).toBeTruthy()
    expect(within(stats).getByText('Shortest window')).toBeTruthy()
    expect(within(stats).getByText('Counter store')).toBeTruthy()
    expect(within(stats).queryByText(/today|this week/i)).toBeNull()
  })

  it('omits the counter store when the catalogue does not serve one', async () => {
    open({ catalogue: { ...catalogue, counterStore: undefined } })

    await screen.findByRole('grid', { name: 'Purposes the app declares' })
    expect(screen.queryByText('Counter store')).toBeNull()
  })

  it('shows a locked-out subject with its count, remaining attempts, fingerprint, and an enabled Reset', async () => {
    const actor = user()
    const { requests } = open({ lookups: [window(5)] })

    await lookUp(actor)

    const result = await screen.findByRole('region', { name: 'Lookup result' })
    expect(within(result).getAllByText('Locked out').length).toBeGreaterThan(0)
    expect(within(result).getByRole('meter').getAttribute('aria-valuetext')).toMatch(/5 of 5 used, 0 remaining/)
    expect(within(result).getByText(fingerprint)).toBeTruthy()
    expect(resetButton().getAttribute('aria-disabled')).not.toBe('true')
    const lookup = requests.find((request) => request.path === 'rate-limiting/lookup')!
    expect(lookup.method).toBe('POST')
    expect(lookup.body).toEqual({ purpose: 'otp.verify', subject })
  })

  it('never puts the subject in a request path or query', async () => {
    const actor = user()
    const { requests } = open({ lookups: [window(5), window(0)] })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: 'Reset this window' }))
    await actor.type(screen.getByRole('textbox', { name: /^Reason/ }), 'Verified by phone')
    await actor.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Reset this window' }))
    await screen.findByText(/0 of 5 used/)

    for (const request of requests) {
      expect(request.path).not.toContain(subject)
      expect(JSON.stringify(request.query ?? {})).not.toContain(subject)
    }
  })

  it('disables Reset with a reason when the subject has no attempts', async () => {
    const actor = user()
    open({ lookups: [window(0)] })

    await lookUp(actor)

    await screen.findByRole('region', { name: 'Lookup result' })
    expect(resetButton().getAttribute('aria-disabled')).toBe('true')
    expect(described(resetButton())).toContain('No attempts in the current window')
  })

  it('disables Reset with a reason for a user who may only view', async () => {
    const actor = user()
    open({ lookups: [window(5)] }, { permissions: { canOperate: false } })

    await lookUp(actor)

    await screen.findByRole('region', { name: 'Lookup result' })
    expect(resetButton().getAttribute('aria-disabled')).toBe('true')
    expect(described(resetButton())).toContain('operate permission')
  })

  it('resets with the reason, the fingerprint id, and the subject argument, then shows the refreshed result', async () => {
    const actor = user()
    const { requests } = open({ lookups: [window(5), window(0)] })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: 'Reset this window' }))
    const dialog = screen.getByRole('alertdialog')
    await actor.type(within(dialog).getByRole('textbox', { name: /^Reason/ }), 'Verified by phone')
    await actor.click(within(dialog).getByRole('button', { name: 'Reset this window' }))

    await screen.findByText(/0 of 5 used/)
    expect(screen.queryByRole('alertdialog')).toBeNull()
    const reset = requests.find((request) => request.path === 'rate-limiting/commands/rate-limiting.reset')!
    expect(reset.body).toEqual({
      ids: [`sign-in.password:${fingerprint}`],
      arguments: { subject },
      reason: 'Verified by phone',
      confirmed: true,
    })
    expect(resetButton().getAttribute('aria-disabled')).toBe('true')
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })

  it('closes the dialog and refreshes when the reset is skipped', async () => {
    const actor = user()
    open({
      lookups: [window(5), window(0)],
      reset: { outcome: 'skipped', reason: 'No attempts in the current window' },
    })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: 'Reset this window' }))
    await actor.type(screen.getByRole('textbox', { name: /^Reason/ }), 'Verified by phone')
    await actor.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Reset this window' }))

    await screen.findByText(/0 of 5 used/)
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('keeps the dialog open with the message on a refusal', async () => {
    const actor = user()
    open({
      lookups: [window(5)],
      reset: new DashboardApiError(
        409,
        "The subject does not match the target's fingerprint; look the subject up again.",
      ),
    })

    await lookUp(actor)
    await confirmReset(actor)

    const alert = await within(screen.getByRole('alertdialog')).findByRole('alert')
    expect(alert.textContent).toMatch(/does not match/)
  })

  it('reads the window again after a failed reset and says its outcome is unknown', async () => {
    const actor = user()
    const reason =
      "The attempt limiter for 'sign-in.password' failed, so the reset's outcome is unknown: the store may have " +
      'removed the counter. Look the subject up again before you retry.'
    const { requests } = open({ lookups: [window(5), window(0)], reset: { outcome: 'failed', reason } })

    await lookUp(actor)
    await confirmReset(actor)

    await screen.findByText(/0 of 5 used/)
    expect(lookups(requests)).toBe(2)
    expect(screen.queryByRole('alertdialog')).toBeNull()
    const result = screen.getByRole('region', { name: 'Lookup result' })
    expect(
      within(result)
        .getAllByRole('status')
        .some((notice) => notice.textContent === reason),
    ).toBe(true)
    expect(document.activeElement).toBe(result)
  })

  it('shows a store failure naming the purpose with Retry and no count', async () => {
    const actor = user()
    open({
      lookups: [new DashboardApiError(503, "The attempt limiter for 'sign-in.password' is unavailable."), window(2)],
    })

    await lookUp(actor)

    expect(await screen.findByRole('button', { name: /Retry/ })).toBeTruthy()
    expect(screen.getAllByText("The attempt limiter for 'sign-in.password' is unavailable.").length).toBeGreaterThan(0)
    expect(screen.queryByRole('meter')).toBeNull()
    await actor.click(screen.getByRole('button', { name: /Retry/ }))
    await screen.findByText(/2 of 5 used/)
  })

  it('reveals the custom quota fields and sends the window in seconds', async () => {
    const actor = user()
    const { requests } = open({ lookups: [window(1, { declared: false })] })

    await actor.selectOptions(await screen.findByRole('combobox', { name: /^Purpose/ }), 'Custom purpose')
    await actor.type(screen.getByRole('textbox', { name: /^Custom purpose/ }), 'export.request')
    await actor.type(screen.getByRole('textbox', { name: /^Limit/ }), '3')
    await actor.type(screen.getByRole('textbox', { name: /^Window/ }), '15')
    await lookUp(actor)

    await screen.findByRole('region', { name: 'Lookup result' })
    const lookup = requests.find((request) => request.path === 'rate-limiting/lookup')!
    expect(lookup.body).toEqual({ purpose: 'export.request', subject, quota: { limit: 3, windowSeconds: 900 } })
  })

  it('clears the result when the subject is edited, so no reset carries the edited text', async () => {
    const actor = user()
    const { requests } = open({ lookups: [window(5)] })

    await lookUp(actor)
    await screen.findByRole('region', { name: 'Lookup result' })
    await actor.type(screen.getByRole('textbox', { name: /^Subject/ }), 'x')

    expect(screen.queryByRole('region', { name: 'Lookup result' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reset this window' })).toBeNull()
    expect(requests.some((request) => request.path.includes('commands'))).toBe(false)
  })

  it('replaces the countdown with a stale note once the window has closed', async () => {
    const actor = user()
    open({ lookups: [window(5)] })

    await lookUp(actor)
    expect(await screen.findByText(/9 m 29 s as of/)).toBeTruthy()
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    expect(screen.queryByText(/9 m 29 s as of/)).toBeNull()
    expect(screen.getAllByText(/window (shown )?has closed/i).length).toBeGreaterThan(0)
  })

  it('announces a completed lookup', async () => {
    const actor = user()
    open({ lookups: [window(5)] })

    await lookUp(actor)

    await screen.findByRole('region', { name: 'Lookup result' })
    expect(screen.getAllByRole('status').some((region) => /Locked out: 5 of 5/.test(region.textContent ?? ''))).toBe(
      true,
    )
  })

  it('names AttemptLimiterOptions.Purposes once, with no empty purposes table, when no purpose is declared', async () => {
    open({ catalogue: { purposes: [] } })

    expect(await screen.findByText(/AttemptLimiterOptions\.Purposes so the screen lists it/)).toBeTruthy()
    expect(screen.getAllByText('No purposes declared')).toHaveLength(1)
    expect(screen.queryByRole('grid', { name: 'Purposes the app declares' })).toBeNull()
  })

  it('drops a lookup answer that arrives after the subject was edited', async () => {
    const actor = user()
    const first = held()
    const { requests } = open({ lookups: [first.answer] })

    await lookUp(actor)
    await actor.type(screen.getByRole('textbox', { name: /^Subject/ }), 'x')
    await act(async () => {
      first.release(window(5))
      await first.answer
    })

    expect(screen.queryByRole('region', { name: 'Lookup result' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reset this window' })).toBeNull()
    expect(requests.some((request) => request.path.includes('commands'))).toBe(false)
  })

  it('keeps the later lookup when an earlier one answers last', async () => {
    const actor = user()
    const first = held()
    const { requests } = open({
      lookups: [first.answer, window(1, { fingerprint: 'Bk3qAbCdEfGhIjKlMnOpQrStUvWxYz0123456789fTb' })],
    })

    await lookUp(actor)
    await actor.clear(screen.getByRole('textbox', { name: /^Subject/ }))
    await lookUp(actor, 'other@example.com')
    await screen.findByText(/1 of 5 used/)
    await act(async () => {
      first.release(window(5))
      await first.answer
    })

    expect(screen.queryByText(/5 of 5 used/)).toBeNull()
    expect(screen.getByText(/1 of 5 used/)).toBeTruthy()
    await confirmReset(actor)
    const reset = requests.find((request) => request.path === 'rate-limiting/commands/rate-limiting.reset')!
    expect(reset.body).toMatchObject({ arguments: { subject: 'other@example.com' } })
  })

  it('refuses Reset and closes its confirmation once the window shown has closed', async () => {
    const actor = user()
    open({ lookups: [window(5)] })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: 'Reset this window' }))
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(resetButton().getAttribute('aria-disabled')).toBe('true')
    expect(described(resetButton())).toContain('Refresh to read the current window')
  })

  it('keeps keyboard focus on the result when the window closes under the open confirmation', async () => {
    const actor = user()
    open({ lookups: [window(5)] })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: 'Reset this window' }))
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })

  it('announces once, in a status notice, that the window shown has closed', async () => {
    const actor = user()
    open({ lookups: [window(5)] })

    await lookUp(actor)
    const result = await screen.findByRole('region', { name: 'Lookup result' })
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    const notices = within(result)
      .getAllByRole('status')
      .filter((notice) => /window shown has closed/.test(notice.textContent ?? ''))
    expect(notices).toHaveLength(1)
    expect(within(result).getAllByText(/has closed/)).toHaveLength(1)
  })

  it('keeps a window longer than the timer limit open until it actually closes', async () => {
    const actor = user()
    const days = 30
    const endsAt = new Date(now.getTime() + days * 24 * 60 * 60 * 1000).toISOString()
    open({
      lookups: [
        window(1, {
          declared: false,
          windowStartsAt: now.toISOString(),
          windowEndsAt: endsAt,
          closesInSeconds: days * 24 * 60 * 60,
          quota: { limit: 5, windowSeconds: days * 24 * 60 * 60 },
        }),
      ],
    })

    await chooseCustom(actor, '5', '720')
    await actor.selectOptions(screen.getByRole('combobox', { name: /^Window unit/ }), 'hours')
    await lookUp(actor)
    await screen.findByRole('region', { name: 'Lookup result' })
    act(() => {
      vi.advanceTimersByTime(1000)
    })

    expect(screen.queryByText(/window shown has closed/)).toBeNull()
    expect(resetButton().getAttribute('aria-disabled')).not.toBe('true')
    act(() => {
      vi.advanceTimersByTime(days * 24 * 60 * 60 * 1000)
    })
    expect(screen.getByText(/window shown has closed/)).toBeTruthy()
  })

  it('blocks a lookup with a blank subject', async () => {
    const actor = user()
    const { requests } = open()

    await actor.click(await screen.findByRole('button', { name: 'Look up' }))

    expect(await screen.findByText('Type the subject to look up.')).toBeTruthy()
    expect(lookups(requests)).toBe(0)
  })

  it.each([
    ['a blank limit', '', '15'],
    ['a zero limit', '0', '15'],
    ['a fractional limit', '1.5', '15'],
    ['a blank window', '3', ''],
    ['a zero window', '3', '0'],
    ['a fractional window', '3', '1.5'],
  ])('blocks a custom lookup with %s', async (_, limit, windowValue) => {
    const actor = user()
    const { requests } = open()

    await chooseCustom(actor, limit, windowValue)
    await lookUp(actor)

    expect(await screen.findByText(/A custom purpose needs its name/)).toBeTruthy()
    expect(lookups(requests)).toBe(0)
  })

  it.each([
    ['Within limit', window(2), 'hd-tone-ok'],
    ['Locked out', window(5), 'hd-tone-fail'],
  ])('shows %s with its tone', async (label, answer, tone) => {
    const actor = user()
    open({ lookups: [answer] })

    await lookUp(actor)

    const result = await screen.findByRole('region', { name: 'Lookup result' })
    expect(within(result).getByRole('heading', { name: label })).toBeTruthy()
    const marks = within(result)
      .getAllByText(label)
      .filter((element) => element.className.includes('hd-tone-'))
    expect(marks).toHaveLength(1)
    expect(marks[0]!.className).toContain(tone)
  })

  it('returns focus to the result after Refresh', async () => {
    const actor = user()
    open({ lookups: [window(5), window(4)] })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: 'Refresh' }))

    await screen.findByText(/4 of 5 used/)
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })

  it('returns focus to the result after Retry', async () => {
    const actor = user()
    open({ lookups: [new DashboardApiError(503, 'The attempt limiter is unavailable.'), window(2)] })

    await lookUp(actor)
    await actor.click(await screen.findByRole('button', { name: /Retry/ }))

    await screen.findByText(/2 of 5 used/)
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })

  it('returns focus to the failure when the refresh after a reset fails, and not again on a later lookup', async () => {
    const actor = user()
    open({ lookups: [window(5), new DashboardApiError(503, 'The attempt limiter is unavailable.'), window(1)] })

    await lookUp(actor)
    await confirmReset(actor)

    await screen.findByRole('button', { name: /Retry/ })
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Lookup result' }))
    await actor.type(screen.getByRole('textbox', { name: /^Subject/ }), 'x')
    await actor.click(screen.getByRole('button', { name: 'Look up' }))
    await screen.findByText(/1 of 5 used/)
    expect(document.activeElement).not.toBe(screen.getByRole('region', { name: 'Lookup result' }))
  })
})
