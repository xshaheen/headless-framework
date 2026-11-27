import { describe, expect, it } from 'vitest'
import { lengthLabel, partsLabel } from '../src/inbox.ts'
import inbox from '../src/module.ts'

describe('dev inbox module definition', () => {
  it('lists Emails, SMS, and Push in one Inbox section with their icons and count keys', () => {
    expect(inbox.sidebar.map((section) => section.label)).toEqual(['Inbox'])
    expect(
      inbox.sidebar[0]!.entries.map(({ label, route, icon, countKey }) => ({ label, route, icon, countKey })),
    ).toEqual([
      { label: 'Emails', route: 'emails', icon: 'mail', countKey: 'emails' },
      { label: 'SMS', route: 'sms', icon: 'message', countKey: 'sms' },
      { label: 'Push', route: 'push', icon: 'bell', countKey: 'push' },
    ])
  })

  it('opens each object kind in its channel’s list', () => {
    expect(inbox.objectKinds.email.route('a b')).toBe('emails?open=a%20b')
    expect(inbox.objectKinds.sms.route('1')).toBe('sms?open=1')
    expect(inbox.objectKinds.push.route('1')).toBe('push?open=1')
  })
})

describe('sms length', () => {
  it('counts a GSM-7 text in septets, an extension character as two', () => {
    // Two euro signs are two characters but four septets: each extension character is sent behind an escape septet.
    expect(lengthLabel({ encoding: 'gsm7', length: 4 })).toBe('4 septets')
    expect(lengthLabel({ encoding: 'gsm7', length: 1 })).toBe('1 septet')
  })

  it('counts a UCS-2 text in UTF-16 code units', () => {
    expect(lengthLabel({ encoding: 'ucs2', length: 100 })).toBe('100 UTF-16 code units')
  })

  it('names one part and several', () => {
    expect(partsLabel(1)).toBe('1 part')
    expect(partsLabel(2)).toBe('2 parts')
  })
})
