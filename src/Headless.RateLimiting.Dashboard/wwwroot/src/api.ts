import type { DashboardApi } from '@headless/dashboard-sdk/data'

/** One declared purpose and its quota, as the catalogue serves it. */
export interface PurposeView {
  name: string
  limit: number
  windowSeconds: number
}

export const counterStores = ['named', 'remote', 'default'] as const
export type CounterStore = (typeof counterStores)[number]

/** The declared purposes, the one with the shortest window, and where the built-in limiter keeps its counters. */
export interface Catalogue {
  purposes: PurposeView[]
  shortest?: PurposeView
  /** Absent under a custom limiter, whose store the module cannot see. */
  counterStore?: CounterStore
  cacheName?: string
}

export interface Quota {
  limit: number
  windowSeconds: number
}

/** One subject's current window. It never carries the subject; `fingerprint` names it. */
export interface LookupView {
  purpose: string
  fingerprint: string
  count: number
  limit: number
  remaining: number
  isLockedOut: boolean
  windowStartsAt: string
  windowEndsAt: string
  closesInSeconds: number
  quota: Quota
  declared: boolean
}

/** What a lookup sent: the reset sends exactly this, so it acts on the subject the result shows. */
export interface LookupCall {
  purpose: string
  subject: string
  /** Only for an undeclared purpose; a declared one always reads with its declared quota. */
  quota?: Quota
}

export const windowUnits = ['seconds', 'minutes', 'hours'] as const
export type WindowUnit = (typeof windowUnits)[number]

const unitSeconds: Record<WindowUnit, number> = { seconds: 1, minutes: 60, hours: 3600 }

/** A window typed as a number and a unit, in seconds; `undefined` when it is not a whole number of at least 1. */
export function toWindowSeconds(value: string, unit: WindowUnit): number | undefined {
  const number = Number(value)

  return Number.isInteger(number) && number >= 1 ? number * unitSeconds[unit] : undefined
}

/** A window in the largest unit that divides it: `15 min`, `1 h`, `90 s`. */
export function formatWindow(seconds: number): string {
  if (seconds % 3600 === 0) {
    return `${seconds / 3600} h`
  }

  return seconds % 60 === 0 ? `${seconds / 60} min` : `${seconds} s`
}

/** A countdown in minutes and seconds: `9 m 29 s`. */
export function formatCountdown(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60

  return minutes > 0 ? `${minutes} m ${rest} s` : `${rest} s`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isPurpose(value: unknown): value is PurposeView {
  return (
    isRecord(value) &&
    typeof value.name === 'string' &&
    typeof value.limit === 'number' &&
    typeof value.windowSeconds === 'number'
  )
}

function readCatalogue(value: unknown): Catalogue {
  if (!isRecord(value) || !Array.isArray(value.purposes) || !value.purposes.every(isPurpose)) {
    throw new Error('The catalogue answer is not a list of purposes.')
  }

  const store = counterStores.find((candidate) => candidate === value.counterStore)

  return {
    purposes: value.purposes,
    shortest: isPurpose(value.shortest) ? value.shortest : undefined,
    counterStore: store,
    cacheName: typeof value.cacheName === 'string' ? value.cacheName : undefined,
  }
}

function readLookup(value: unknown): LookupView {
  if (
    !isRecord(value) ||
    typeof value.purpose !== 'string' ||
    typeof value.fingerprint !== 'string' ||
    typeof value.count !== 'number' ||
    typeof value.limit !== 'number' ||
    typeof value.remaining !== 'number' ||
    typeof value.isLockedOut !== 'boolean' ||
    typeof value.windowStartsAt !== 'string' ||
    typeof value.windowEndsAt !== 'string' ||
    typeof value.closesInSeconds !== 'number' ||
    !isRecord(value.quota) ||
    typeof value.quota.limit !== 'number' ||
    typeof value.quota.windowSeconds !== 'number' ||
    typeof value.declared !== 'boolean'
  ) {
    throw new Error('The lookup answer is not a subject’s window.')
  }

  return {
    purpose: value.purpose,
    fingerprint: value.fingerprint,
    count: value.count,
    limit: value.limit,
    remaining: value.remaining,
    isLockedOut: value.isLockedOut,
    windowStartsAt: value.windowStartsAt,
    windowEndsAt: value.windowEndsAt,
    closesInSeconds: value.closesInSeconds,
    quota: { limit: value.quota.limit, windowSeconds: value.quota.windowSeconds },
    declared: value.declared,
  }
}

export async function getCatalogue(api: DashboardApi): Promise<Catalogue> {
  return readCatalogue(await api.request<unknown>({ method: 'GET', path: 'rate-limiting/catalogue' }))
}

/** A POST, so the subject (an email or a phone number) never reaches a URL, a query string, or a request log. */
export async function lookUp(api: DashboardApi, call: LookupCall): Promise<LookupView> {
  return readLookup(await api.request<unknown>({ method: 'POST', path: 'rate-limiting/lookup', body: call }))
}

export const resetCommandId = 'rate-limiting.reset'

/** The reset's target id: the purpose and the fingerprint, so the operator log never holds the subject. */
export function subjectId(view: LookupView): string {
  return `${view.purpose}:${view.fingerprint}`
}

/** The reset's arguments: the subject, and the quota only for an undeclared purpose. */
export function resetArguments(call: LookupCall): Record<string, string | number> {
  return call.quota
    ? { subject: call.subject, limit: call.quota.limit, windowSeconds: call.quota.windowSeconds }
    : { subject: call.subject }
}
