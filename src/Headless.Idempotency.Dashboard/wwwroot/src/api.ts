import type { DashboardStateRole } from '@headless/dashboard-sdk'
import type { DashboardApi } from '@headless/dashboard-sdk/data'

/** The store this host runs on and its admission defaults, every duration in whole seconds. */
export interface StoreView {
  provider: string
  /** The qualified table, or the cache key prefix; absent in memory. */
  location?: string
  defaultRetentionSeconds: number
  defaultLeaseSeconds: number
  minimumLeaseSeconds: number
  maximumLeaseSeconds: number
  /** Absent when the purge is off, or when the records expire on their own (the cache provider). */
  purgeIntervalSeconds?: number
  purgeBatchSize: number
}

export const recordStatuses = ['Pending', 'Completed', 'Released'] as const
export type RecordStatus = (typeof recordStatuses)[number]

export interface Fingerprint {
  algorithm: string
  /** The digest as lowercase hex. */
  hash: string
}

/** One key's record metadata. It never carries the stored result or recovery-state bytes. */
export interface RecordView {
  status: RecordStatus
  generation?: number
  /** Absent on the cache provider, which does not count takeovers. */
  takeoverCount?: number
  fingerprint: Fingerprint
  leaseExpiresAt?: string
  isLeaseExpired: boolean
  retentionUntil: string
  /** The store's clock when it read the record; every time judgement compares with it. */
  observedAt: string
  resultContract?: string
  resultSizeBytes?: number
  recoveryPoint?: string
  recoveryContract?: string
  recoveryStateSizeBytes?: number
}

/** A lookup's answer. It never echoes the key. */
export type LookupView = { tenantId?: string; found: false } | { tenantId?: string; found: true; record: RecordView }

/** What a lookup sends: a blank tenant is left out, which reads the host scope. */
export interface LookupCall {
  key: string
  tenantId?: string
}

export function lookupCall(key: string, tenant: string): LookupCall {
  return tenant ? { key, tenantId: tenant } : { key }
}

export const recordStates = ['completed', 'in-flight', 'past-retention', 'stalled', 'released', 'not-admitted'] as const
export type RecordState = (typeof recordStates)[number]

export interface StateLabel {
  state: RecordState
  label: string
  tone: DashboardStateRole
  /** What the next admission of the key does, in one sentence. */
  meaning: string
}

const labels: Record<RecordState, Omit<StateLabel, 'state'>> = {
  completed: { label: 'Completed', tone: 'ok', meaning: 'Later admissions with this key replay the stored result.' },
  'in-flight': {
    label: 'In flight',
    tone: 'enq',
    meaning: 'An attempt owns the key under a live lease. A caller with the same key gets InFlight and retries later.',
  },
  'past-retention': {
    label: 'In flight',
    tone: 'enq',
    meaning:
      'An attempt owns the key under a live lease. Its retention has passed, so the record is kept past retention until the attempt ends.',
  },
  stalled: {
    label: 'Stalled',
    tone: 'proc',
    meaning: 'The attempt holding the key stopped renewing its lease. The next admission takes the key over.',
  },
  released: {
    label: 'Released',
    tone: 'off',
    meaning: 'No attempt holds the key. The next admission runs the operation again.',
  },
  'not-admitted': {
    label: 'Not admitted yet',
    tone: 'off',
    meaning: 'The record exists, but no attempt was admitted on it yet.',
  },
}

/** Names a found record's state from the server's fields only; the browser clock never decides it. */
export function recordState(record: RecordView): StateLabel {
  const state = stateOf(record)

  return { state, ...labels[state] }
}

function stateOf(record: RecordView): RecordState {
  if (record.status === 'Completed') {
    return 'completed'
  }

  if (record.isLeaseExpired) {
    return 'stalled'
  }

  if (record.status === 'Pending') {
    return Date.parse(record.retentionUntil) <= Date.parse(record.observedAt) ? 'past-retention' : 'in-flight'
  }

  return record.generation === undefined ? 'not-admitted' : 'released'
}

/** A duration in the largest whole unit: `24 h`, `2 min`, `90 s`. */
export function formatDuration(seconds: number): string {
  if (seconds > 0 && seconds % 86400 === 0) {
    return `${seconds / 86400} d`
  }

  if (seconds > 0 && seconds % 3600 === 0) {
    return `${seconds / 3600} h`
  }

  return seconds > 0 && seconds % 60 === 0 ? `${seconds / 60} min` : `${seconds} s`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function optionalString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

function optionalNumber(value: unknown): number | undefined {
  return typeof value === 'number' ? value : undefined
}

function readStore(value: unknown): StoreView {
  if (
    !isRecord(value) ||
    typeof value.provider !== 'string' ||
    typeof value.defaultRetentionSeconds !== 'number' ||
    typeof value.defaultLeaseSeconds !== 'number' ||
    typeof value.minimumLeaseSeconds !== 'number' ||
    typeof value.maximumLeaseSeconds !== 'number' ||
    typeof value.purgeBatchSize !== 'number'
  ) {
    throw new Error('The store answer is not a store description.')
  }

  return {
    provider: value.provider,
    location: optionalString(value.location),
    defaultRetentionSeconds: value.defaultRetentionSeconds,
    defaultLeaseSeconds: value.defaultLeaseSeconds,
    minimumLeaseSeconds: value.minimumLeaseSeconds,
    maximumLeaseSeconds: value.maximumLeaseSeconds,
    purgeIntervalSeconds: optionalNumber(value.purgeIntervalSeconds),
    purgeBatchSize: value.purgeBatchSize,
  }
}

function readRecord(value: unknown): RecordView {
  const status = isRecord(value) ? recordStatuses.find((candidate) => candidate === value.status) : undefined

  if (
    !isRecord(value) ||
    status === undefined ||
    !isRecord(value.fingerprint) ||
    typeof value.fingerprint.algorithm !== 'string' ||
    typeof value.fingerprint.hash !== 'string' ||
    typeof value.isLeaseExpired !== 'boolean' ||
    typeof value.retentionUntil !== 'string' ||
    typeof value.observedAt !== 'string'
  ) {
    throw new Error('The lookup answer is not an idempotency record.')
  }

  return {
    status,
    generation: optionalNumber(value.generation),
    takeoverCount: optionalNumber(value.takeoverCount),
    fingerprint: { algorithm: value.fingerprint.algorithm, hash: value.fingerprint.hash },
    leaseExpiresAt: optionalString(value.leaseExpiresAt),
    isLeaseExpired: value.isLeaseExpired,
    retentionUntil: value.retentionUntil,
    observedAt: value.observedAt,
    resultContract: optionalString(value.resultContract),
    resultSizeBytes: optionalNumber(value.resultSizeBytes),
    recoveryPoint: optionalString(value.recoveryPoint),
    recoveryContract: optionalString(value.recoveryContract),
    recoveryStateSizeBytes: optionalNumber(value.recoveryStateSizeBytes),
  }
}

function readLookup(value: unknown): LookupView {
  if (!isRecord(value) || typeof value.found !== 'boolean') {
    throw new Error('The lookup answer is not an idempotency lookup.')
  }

  const tenantId = optionalString(value.tenantId)

  return value.found ? { tenantId, found: true, record: readRecord(value.record) } : { tenantId, found: false }
}

export async function getStore(api: DashboardApi): Promise<StoreView> {
  return readStore(await api.request<unknown>({ method: 'GET', path: 'idempotency/store' }))
}

/** A POST, so the key (often an order or payment reference) never reaches a URL, a query string, or a request log. */
export async function lookUp(api: DashboardApi, call: LookupCall): Promise<LookupView> {
  return readLookup(await api.request<unknown>({ method: 'POST', path: 'idempotency/lookup', body: call }))
}
