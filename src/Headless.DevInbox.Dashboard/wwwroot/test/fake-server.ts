import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { ChannelKind, EmailDetail, PushDetail, SmsDetail } from '../src/inbox.ts'
import inbox from '../src/module.ts'

export const now = new Date('2026-11-26T10:00:00Z')

let sequence = 0

function next() {
  sequence += 1

  return { id: `00000000-0000-0000-0000-${String(sequence).padStart(12, '0')}`, sequence }
}

export function email(overrides: Partial<EmailDetail> = {}): EmailDetail {
  return {
    ...next(),
    capturedAt: '2026-11-26T09:58:00Z',
    senderName: '',
    failed: false,
    from: { address: 'app@example.com', displayName: 'The App' },
    to: [{ address: 'ada@example.com' }],
    cc: [],
    bcc: [],
    subject: 'Welcome aboard',
    hasHtml: false,
    textBody: 'Hello there.',
    attachments: [],
    ...overrides,
  }
}

export function sms(overrides: Partial<SmsDetail> = {}): SmsDetail {
  return {
    ...next(),
    capturedAt: '2026-11-26T09:58:00Z',
    senderName: '',
    failed: false,
    recipients: ['+201000000001'],
    isBulk: false,
    text: 'Your code is 123456',
    segmentation: { encoding: 'gsm7', length: 19, parts: 1 },
    ...overrides,
  }
}

export function push(overrides: Partial<PushDetail> = {}): PushDetail {
  return {
    ...next(),
    capturedAt: '2026-11-26T09:58:00Z',
    senderName: '',
    failed: false,
    targets: ['device-token-1'],
    isMulticast: false,
    title: 'Order shipped',
    body: 'Your order is on its way.',
    isDataOnly: false,
    data: { orderId: '42' },
    payloadSizeBytes: 120,
    exceedsPayloadLimit: false,
    maxPayloadBytes: 4096,
    ...overrides,
  }
}

export function emailRow(detail: EmailDetail) {
  return {
    id: detail.id,
    sequence: detail.sequence,
    capturedAt: detail.capturedAt,
    senderName: detail.senderName,
    failed: detail.failed,
    from: detail.from,
    recipient: detail.to[0]?.address,
    recipientCount: detail.to.length + detail.cc.length + detail.bcc.length,
    subject: detail.subject,
    hasHtml: detail.hasHtml,
    attachmentCount: detail.attachments.length,
  }
}

function smsRow(detail: SmsDetail) {
  return {
    id: detail.id,
    sequence: detail.sequence,
    capturedAt: detail.capturedAt,
    senderName: detail.senderName,
    failed: detail.failed,
    recipient: detail.recipients[0],
    recipientCount: detail.recipients.length,
    isBulk: detail.isBulk,
    snippet: detail.text,
    encoding: detail.segmentation.encoding,
    parts: detail.segmentation.parts,
  }
}

function pushRow(detail: PushDetail) {
  return {
    id: detail.id,
    sequence: detail.sequence,
    capturedAt: detail.capturedAt,
    senderName: detail.senderName,
    failed: detail.failed,
    target: detail.targets[0],
    targetCount: detail.targets.length,
    title: detail.title,
    body: detail.body,
    isDataOnly: detail.isDataOnly,
    dataKeys: Object.keys(detail.data ?? {}),
    payloadSizeBytes: detail.payloadSizeBytes,
    exceedsPayloadLimit: detail.exceedsPayloadLimit,
  }
}

function definition(kind: ChannelKind, name: 'delete' | 'clear'): CommandDefinition {
  return {
    id: `dev-inbox.${kind}.${name}`,
    label: name === 'delete' ? 'Delete' : 'Clear',
    objectKind: kind,
    permission: 'operate',
    confirmation: 'confirm',
    typedConfirmationAbove: 100,
    requiresReason: name === 'clear',
    supportsFilter: true,
    arguments: { type: 'object', properties: {}, required: [] },
    canRun: true,
  }
}

export interface FakeServer {
  emails?: EmailDetail[]
  sms?: SmsDetail[]
  push?: PushDetail[]
  channels?: ChannelKind[]
}

const paths: Readonly<Record<string, ChannelKind>> = { emails: 'email', sms: 'sms', push: 'push' }

function server(fake: FakeServer) {
  const stored = { email: fake.emails ?? [], sms: fake.sms ?? [], push: fake.push ?? [] }
  const channels = fake.channels ?? (['email', 'sms', 'push'] as const)
  const commands = channels.flatMap((kind) => [definition(kind, 'delete'), definition(kind, 'clear')])

  return (request: ApiRequest): unknown => {
    const query = (request.query ?? {}) as Readonly<Record<string, string | undefined>>

    if (request.path === 'dev-inbox/channels') {
      return { channels: channels.map((kind) => ({ kind, capacity: 500 })) }
    }

    if (request.path === 'dev-inbox/commands') {
      return { commands }
    }

    const run = /^dev-inbox\/commands\/([^/]+)$/.exec(request.path)

    if (request.method === 'POST' && run) {
      return { rows: (request.body as { ids: string[] }).ids.map((id) => ({ id, outcome: 'done' })) }
    }

    const list = /^dev-inbox\/(emails|sms|push)$/.exec(request.path)

    if (request.method === 'GET' && list) {
      const kind = paths[list[1]!]!
      const search = query.search?.toLowerCase()
      const rows =
        kind === 'email'
          ? stored.email.filter((item) => !search || item.subject.toLowerCase().includes(search)).map(emailRow)
          : kind === 'sms'
            ? stored.sms.filter((item) => !search || item.text.toLowerCase().includes(search)).map(smsRow)
            : stored.push.map(pushRow)

      return { items: [...rows].sort((a, b) => b.sequence - a.sequence), nextCursor: null }
    }

    const one = /^dev-inbox\/(emails|sms|push)\/([^/]+)$/.exec(request.path)

    if (request.method === 'GET' && one) {
      const kind = paths[one[1]!]!
      const found = (stored[kind] as { id: string }[]).find((item) => item.id === decodeURIComponent(one[2]!))

      if (!found) {
        throw new DashboardApiError(404, 'Not found')
      }

      return found
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(inbox, { api: server(fake), ...options })
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}

export function described(button: HTMLElement) {
  return document.getElementById(button.getAttribute('aria-describedby')!)?.textContent
}
