import { useDashboard, useModule } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import type { DashboardApi, DashboardListPage } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'
import type { ListPageRequest } from '@headless/dashboard-sdk/table'

export type ChannelKind = 'email' | 'sms' | 'push'

export const channelPaths: Readonly<Record<ChannelKind, string>> = { email: 'emails', sms: 'sms', push: 'push' }

export const pageSize = 50

export const viewOnlyReason = 'Needs operate permission'

export function topicOf(kind: ChannelKind) {
  return `dev-inbox.${kind}`
}

/** What every capture row carries: the list keys rows by `id` and orders versions by the store's sequence. */
export interface CaptureRow {
  id: string
  sequence: number
  version?: string
  capturedAt: string
  senderName: string
  failed: boolean
}

export interface Address {
  address: string
  displayName?: string | null
}

export interface EmailRow extends CaptureRow {
  from: Address
  recipient?: string | null
  recipientCount: number
  subject: string
  hasHtml: boolean
  attachmentCount: number
}

export interface EmailAttachment {
  index: number
  fileName: string
  contentType: string
  sizeBytes: number
  isOmitted: boolean
}

export interface EmailDetail extends CaptureRow {
  from: Address
  to: Address[]
  cc: Address[]
  bcc: Address[]
  subject: string
  hasHtml: boolean
  textBody?: string | null
  attachments: EmailAttachment[]
  failureError?: string | null
  traceId?: string | null
  correlationId?: string | null
}

export type SmsEncoding = 'gsm7' | 'ucs2'

export interface Segmentation {
  encoding: SmsEncoding
  length: number
  parts: number
}

export interface SmsRow extends CaptureRow {
  recipient?: string | null
  recipientCount: number
  isBulk: boolean
  snippet: string
  encoding: SmsEncoding
  parts: number
}

export interface SmsDetail extends CaptureRow {
  recipients: string[]
  isBulk: boolean
  text: string
  segmentation: Segmentation
  messageId?: string | null
  properties?: Record<string, string | null> | null
  failureError?: string | null
  traceId?: string | null
  correlationId?: string | null
}

export interface PushRow extends CaptureRow {
  target?: string | null
  targetCount: number
  title?: string | null
  body?: string | null
  isDataOnly: boolean
  dataKeys: string[]
  payloadSizeBytes: number
  exceedsPayloadLimit: boolean
}

export interface PushDetail extends CaptureRow {
  targets: string[]
  isMulticast: boolean
  title?: string | null
  body?: string | null
  isDataOnly: boolean
  data?: Record<string, string> | null
  collapseKey?: string | null
  badge?: number | null
  sound?: string | null
  priority?: string | null
  timeToLiveSeconds?: number | null
  payloadSizeBytes: number
  exceedsPayloadLimit: boolean
  maxPayloadBytes: number
  failureError?: string | null
  traceId?: string | null
  correlationId?: string | null
}

interface ServedPage<T> {
  items: T[]
  nextCursor?: string | null
}

/** A capture carries its sequence as its version, so a change the stream already sent never patches it back. */
function versioned<T extends CaptureRow>(row: T): T {
  return { ...row, version: String(row.sequence) }
}

/** One page of a channel's list, newest first; the cursor is the sequence the next page starts below. */
export async function fetchPage<T extends CaptureRow>(
  api: DashboardApi,
  kind: ChannelKind,
  cursor: string | undefined,
  request: Pick<ListPageRequest, 'search'>,
): Promise<DashboardListPage<T>> {
  const page = await api.request<ServedPage<T>>({
    method: 'GET',
    path: `dev-inbox/${channelPaths[kind]}`,
    query: {
      limit: String(pageSize),
      ...(request.search ? { search: request.search } : {}),
      ...(cursor ? { before: cursor } : {}),
    },
  })

  return { items: page.items.map(versioned), nextCursor: page.nextCursor ?? null }
}

export async function fetchDetail<T extends CaptureRow>(api: DashboardApi, kind: ChannelKind, id: string): Promise<T> {
  return versioned(
    await api.request<T>({ method: 'GET', path: `dev-inbox/${channelPaths[kind]}/${encodeURIComponent(id)}` }),
  )
}

export function useChannels() {
  const { api } = useDashboard()

  return useQuery({
    queryKey: ['dev-inbox', 'channels'],
    queryFn: async () =>
      (
        await api.request<{ channels: { kind: ChannelKind; capacity: number }[] }>({
          method: 'GET',
          path: 'dev-inbox/channels',
        })
      ).channels,
    staleTime: Infinity,
  })
}

export function useChannelCommands(kind: ChannelKind) {
  const { api } = useDashboard()
  const module = useModule()
  const commands = useQuery({
    queryKey: ['commands', module.id],
    queryFn: () => createCommandsApi(api, module.id).list(),
    staleTime: Infinity,
  })
  const all = commands.data ?? []

  function find(name: 'delete' | 'clear'): CommandDefinition | undefined {
    const command = all.find((candidate) => candidate.id === `${module.id}.${kind}.${name}`)

    if (!command || module.canOperate) {
      return command
    }

    return { ...command, canRun: false, disabledReason: command.disabledReason ?? viewOnlyReason }
  }

  return { all, delete: find('delete'), clear: find('clear') }
}

export const channelNouns: Readonly<Record<ChannelKind, { one: string; many: string; setup: string }>> = {
  email: { one: 'email', many: 'emails', setup: 'AddHeadlessEmails' },
  sms: { one: 'SMS', many: 'SMS', setup: 'AddHeadlessSms' },
  push: { one: 'push', many: 'push notifications', setup: 'AddHeadlessPushNotifications' },
}

export function encodingName(encoding: SmsEncoding) {
  return encoding === 'gsm7' ? 'GSM-7' : 'UCS-2'
}

/**
 * An SMS's length in the units its encoding counts: GSM-7 septets, where an extension character such as `€` takes two,
 * or UCS-2's UTF-16 code units, where a character outside the basic plane takes two. A count of characters would
 * misread why a message split.
 */
export function lengthLabel({ encoding, length }: Pick<Segmentation, 'encoding' | 'length'>) {
  const count = length.toLocaleString('en-US')

  return encoding === 'gsm7'
    ? `${count} ${length === 1 ? 'septet' : 'septets'}`
    : `${count} UTF-16 ${length === 1 ? 'code unit' : 'code units'}`
}

export function partsLabel(parts: number) {
  return `${parts.toLocaleString('en-US')} ${parts === 1 ? 'part' : 'parts'}`
}

/** The sender instance that captured it; the default instance has no name. */
export function senderLabel(senderName: string) {
  return senderName || 'default'
}

export function addressLabel(address: Address) {
  return address.displayName ? `${address.displayName} <${address.address}>` : address.address
}
