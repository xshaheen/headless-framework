import { screen } from '@testing-library/react'
import { axeViolations } from '@headless/dashboard-sdk/testing/axe'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { email, now, open, push, sms } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

// The open email has no HTML body: axe cannot check inside a frame under jsdom, and the frame is the SDK's own.
const opened = { email: email(), sms: sms(), push: push() }
const captures = { emails: [opened.email], sms: [opened.sms], push: [opened.push] }

describe('dev inbox accessibility', () => {
  it.each(['emails', 'sms', 'push'])('the %s view breaks no WCAG A or AA rule axe checks', async (path) => {
    open(captures, { path })

    await screen.findByRole('heading', { level: 1 })
    expect(await axeViolations()).toEqual([])
  })

  it.each([
    ['emails', opened.email.id, 'Email'],
    ['sms', opened.sms.id, 'SMS'],
    ['push', opened.push.id, 'Push'],
  ])('the %s view with a capture open breaks no rule', async (path, id, label) => {
    open(captures, { path: `${path}?open=${id}` })

    await screen.findByRole('button', { name: 'Delete' })
    expect(screen.getByRole('region', { name: `${label} ${id}` })).toBeTruthy()
    expect(await axeViolations()).toEqual([])
  })
})
