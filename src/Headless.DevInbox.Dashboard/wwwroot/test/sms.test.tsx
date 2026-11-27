import { screen, within } from '@testing-library/react'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { now, open, sms, user } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

describe('sms view', () => {
  it('lists recipient, text, sender, and parts, and a bulk SMS by its recipient count', async () => {
    open(
      {
        sms: [
          sms({ text: 'Your code is 123456', senderName: 'otp' }),
          sms({ text: 'Sale today', isBulk: true, recipients: ['+201', '+202', '+203'] }),
        ],
      },
      { path: 'sms' },
    )

    const code = await screen.findByRole('row', { name: /\+201000000001/ })
    expect(within(code).getByText('Your code is 123456')).toBeTruthy()
    expect(within(code).getByText('otp')).toBeTruthy()
    expect(within(code).getByText('GSM-7 · 1 part')).toBeTruthy()
    expect(within(screen.getByRole('row', { name: /3 recipients/ })).getByText('Sale today')).toBeTruthy()
  })

  it('reads a GSM-7 SMS in septets, an extension character counting two, and one part', async () => {
    const euro = sms({ text: '€€', segmentation: { encoding: 'gsm7', length: 4, parts: 1 } })
    open({ sms: [euro] }, { path: `sms?open=${euro.id}` })

    await screen.findByText('4 septets')
    const panel = screen.getByRole('region', { name: `SMS ${euro.id}` })
    expect(within(panel).getByText('GSM-7')).toBeTruthy()
    expect(within(panel).getByText('4 septets')).toBeTruthy()
    expect(within(panel).getByText('1 part')).toBeTruthy()
  })

  it('reads a Unicode SMS as UCS-2 in UTF-16 code units with its parts', async () => {
    const arabic = sms({ text: 'م'.repeat(100), segmentation: { encoding: 'ucs2', length: 100, parts: 2 } })
    open({ sms: [arabic] }, { path: `sms?open=${arabic.id}` })

    await screen.findByText('100 UTF-16 code units')
    const panel = screen.getByRole('region', { name: `SMS ${arabic.id}` })
    expect(within(panel).getByText('UCS-2')).toBeTruthy()
    expect(within(panel).getByText('100 UTF-16 code units')).toBeTruthy()
    expect(within(panel).getByText('2 parts')).toBeTruthy()
  })

  it('shows a failed SMS’s error', async () => {
    const failed = sms({ failed: true, failureError: 'Access to the path is denied.' })
    open({ sms: [failed] }, { path: `sms?open=${failed.id}` })

    expect(await screen.findByText('The send failed: Access to the path is denied.')).toBeTruthy()
  })

  it('starts Clear as an Operation that asks for a reason and counts the scope before it runs', async () => {
    const actor = user()
    const view = open({ sms: [sms(), sms()] }, { path: 'sms' })
    await screen.findAllByRole('row', { name: /\+201000000001/ })

    await actor.click(screen.getByRole('button', { name: 'Clear' }))

    // The confirm asks for the reason first; nothing is counted or run until the operator gives one.
    expect(await screen.findByRole('textbox', { name: /Reason/ })).toBeTruthy()
    expect(await screen.findByText(/Clear captured SMS/)).toBeTruthy()
    expect(view.requests.some((request) => request.path.endsWith('/confirm') || request.path.endsWith('/chunks'))).toBe(
      false,
    )
  })
})
