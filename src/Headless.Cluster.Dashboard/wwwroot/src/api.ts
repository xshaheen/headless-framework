import { useDashboard, useModule, type DashboardStateRole } from '@headless/dashboard-sdk'
import { createCommandsApi, type CommandDefinition } from '@headless/dashboard-sdk/commands'
import type { DashboardApi } from '@headless/dashboard-sdk/data'
import { useQuery } from '@headless/dashboard-sdk/query'

export const nodeStates = ['Alive', 'Suspected', 'Dead'] as const
export type NodeState = (typeof nodeStates)[number]

export const healthStatuses = ['Healthy', 'Degraded', 'Unhealthy'] as const
export type HealthStatus = (typeof healthStatuses)[number]

/** A check filter: a health status, or `NotProbed` for a check with no recorded result. */
export const checkFilters = [...healthStatuses, 'NotProbed'] as const
export type CheckFilter = (typeof checkFilters)[number]

export const viewOnlyReason = 'Needs operate permission'

export interface CoordinationSummary {
  clusterName: string
  providerName: string
  role: string | null
  localNode: string | null
  heartbeatIntervalMilliseconds: number
  suspicionThresholdMilliseconds: number
  deadThresholdMilliseconds: number
  deadRetentionWindowMilliseconds: number
  selfFenceMarginMilliseconds: number
  membershipHeartbeatEnabled: boolean
}

export interface ClusterSummary {
  /** The serving host: its node identity when it is a member, else its machine name. */
  host: string
  coordination: CoordinationSummary | null
  health: { registered: boolean; monitor: boolean } | null
}

export interface ClusterNode {
  id: string
  nodeId: string
  incarnation: number
  state: NodeState
  role: string | null
  lastHeartbeatAt: string
  isLocal: boolean
  ownerTag: string
  metadata: Readonly<Record<string, string>>
}

export interface ValueGroup {
  value: string | null
  nodes: string[]
}

export interface NodeAgreement {
  key: string
  isUnanimous: boolean
  value: string
  matches: number
  compared: number
  groups: ValueGroup[]
}

export interface MembershipEvent {
  sequence: number
  kind: string
  node: string
  observedAt: string
  state: NodeState | null
  lastHeartbeatAt: string | null
  isFirstObservation: boolean
}

export interface NodeDetail {
  node: ClusterNode
  agreement: NodeAgreement[]
  events: MembershipEvent[]
}

export interface EventLog {
  observer: string
  clock: string
  items: MembershipEvent[]
}

export interface ProbeResult {
  status: HealthStatus
  durationMilliseconds: number
  description: string | null
  completedAt: string
}

export interface HealthCheck {
  /** The check's name, as the row and panel id. */
  id: string
  name: string
  tags: string[]
  failureStatus: HealthStatus
  timeoutMilliseconds: number | null
  isLiveness: boolean
  latest: ProbeResult | null
}

export interface CheckDetail {
  check: HealthCheck
  results: ProbeResult[]
}

export interface HostHealth {
  host: string
  status: HealthStatus | null
  probedAt: string | null
  totals: { healthy: number; degraded: number; unhealthy: number; notProbed: number }
  liveness: { healthy: number; total: number } | null
  slowest: { name: string; durationMilliseconds: number } | null
}

/** The server answered a shape this module does not read, so the view shows the read as failed. */
function shapeError(what: string) {
  return new Error(`The server answered an unexpected ${what}.`)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function record(value: unknown, what: string): Record<string, unknown> {
  if (!isRecord(value)) {
    throw shapeError(what)
  }

  return value
}

function text(value: unknown, what: string): string {
  if (typeof value !== 'string') {
    throw shapeError(what)
  }

  return value
}

/** A field the server writes as `null` when it has none; a missing field reads the same. */
function optionalText(value: unknown, what: string): string | null {
  return value == null ? null : text(value, what)
}

function count(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw shapeError(what)
  }

  return value
}

function flag(value: unknown, what: string): boolean {
  if (typeof value !== 'boolean') {
    throw shapeError(what)
  }

  return value
}

function list<T>(value: unknown, what: string, read: (item: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw shapeError(what)
  }

  return value.map(read)
}

function oneOf<const T extends readonly string[]>(values: T, value: unknown, what: string): T[number] {
  const found = values.find((candidate) => candidate === value)

  if (found === undefined) {
    throw shapeError(what)
  }

  return found
}

function readCoordination(value: unknown): CoordinationSummary {
  const item = record(value, 'coordination summary')

  return {
    clusterName: text(item.clusterName, 'cluster name'),
    providerName: text(item.providerName, 'provider name'),
    role: optionalText(item.role, 'role'),
    localNode: optionalText(item.localNode, 'local node'),
    heartbeatIntervalMilliseconds: count(item.heartbeatIntervalMilliseconds, 'heartbeat interval'),
    suspicionThresholdMilliseconds: count(item.suspicionThresholdMilliseconds, 'suspicion threshold'),
    deadThresholdMilliseconds: count(item.deadThresholdMilliseconds, 'dead threshold'),
    deadRetentionWindowMilliseconds: count(item.deadRetentionWindowMilliseconds, 'dead retention window'),
    selfFenceMarginMilliseconds: count(item.selfFenceMarginMilliseconds, 'self-fence margin'),
    membershipHeartbeatEnabled: flag(item.membershipHeartbeatEnabled, 'heartbeat flag'),
  }
}

export function readSummary(value: unknown): ClusterSummary {
  const item = record(value, 'summary')
  const health = item.health == null ? null : record(item.health, 'health registration')

  return {
    host: text(item.host, 'host'),
    coordination: item.coordination == null ? null : readCoordination(item.coordination),
    health: health && {
      registered: flag(health.registered, 'health registration'),
      monitor: flag(health.monitor, 'monitor registration'),
    },
  }
}

function readMetadata(value: unknown): Record<string, string> {
  const item = record(value, 'metadata')

  return Object.fromEntries(Object.entries(item).map(([key, entry]) => [key, text(entry, 'metadata value')]))
}

export function readNode(value: unknown): ClusterNode {
  const item = record(value, 'node')

  return {
    id: text(item.id, 'node id'),
    nodeId: text(item.nodeId, 'node id'),
    incarnation: count(item.incarnation, 'incarnation'),
    state: oneOf(nodeStates, item.state, 'node state'),
    role: optionalText(item.role, 'role'),
    lastHeartbeatAt: text(item.lastHeartbeatAt, 'last heartbeat'),
    isLocal: flag(item.isLocal, 'local flag'),
    ownerTag: text(item.ownerTag, 'owner tag'),
    metadata: readMetadata(item.metadata ?? {}),
  }
}

function readGroup(value: unknown): ValueGroup {
  const item = record(value, 'value group')

  return {
    value: optionalText(item.value, 'group value'),
    nodes: list(item.nodes, 'group nodes', (node) => text(node, 'group node')),
  }
}

function readEvent(value: unknown): MembershipEvent {
  const item = record(value, 'membership event')

  return {
    sequence: count(item.sequence, 'event sequence'),
    kind: text(item.kind, 'event kind'),
    node: text(item.node, 'event node'),
    observedAt: text(item.observedAt, 'event time'),
    state: item.state == null ? null : oneOf(nodeStates, item.state, 'event state'),
    lastHeartbeatAt: optionalText(item.lastHeartbeatAt, 'event heartbeat'),
    isFirstObservation: flag(item.isFirstObservation, 'first observation flag'),
  }
}

export function readNodeDetail(value: unknown): NodeDetail {
  const item = record(value, 'node detail')

  return {
    node: readNode(item.node),
    agreement: list(item.agreement, 'agreement', (entry) => {
      const key = record(entry, 'agreement key')

      return {
        key: text(key.key, 'agreement key'),
        isUnanimous: flag(key.isUnanimous, 'agreement flag'),
        value: text(key.value, 'agreement value'),
        matches: count(key.matches, 'agreement matches'),
        compared: count(key.compared, 'agreement compared'),
        groups: list(key.groups, 'agreement groups', readGroup),
      }
    }),
    events: list(item.events, 'node events', readEvent),
  }
}

export function readEventLog(value: unknown): EventLog {
  const item = record(value, 'event log')

  return {
    observer: text(item.observer, 'observer'),
    clock: text(item.clock, 'clock'),
    items: list(item.items, 'events', readEvent),
  }
}

function readResult(value: unknown): ProbeResult {
  const item = record(value, 'probe result')

  return {
    status: oneOf(healthStatuses, item.status, 'probe status'),
    durationMilliseconds: count(item.durationMilliseconds, 'probe duration'),
    description: optionalText(item.description, 'probe description'),
    completedAt: text(item.completedAt, 'probe time'),
  }
}

export function readCheck(value: unknown): HealthCheck {
  const item = record(value, 'health check')

  const name = text(item.name, 'check name')

  return {
    id: name,
    name,
    tags: list(item.tags, 'check tags', (tag) => text(tag, 'check tag')),
    failureStatus: oneOf(healthStatuses, item.failureStatus, 'failure status'),
    timeoutMilliseconds: item.timeoutMilliseconds == null ? null : count(item.timeoutMilliseconds, 'timeout'),
    isLiveness: flag(item.isLiveness, 'liveness flag'),
    latest: item.latest == null ? null : readResult(item.latest),
  }
}

export function readCheckDetail(value: unknown): CheckDetail {
  const item = record(value, 'check detail')

  return { check: readCheck(item.check), results: list(item.results, 'probe results', readResult) }
}

export function readHealth(value: unknown): HostHealth {
  const item = record(value, 'health')
  const totals = record(item.totals, 'health totals')
  const liveness = item.liveness == null ? null : record(item.liveness, 'liveness')
  const slowest = item.slowest == null ? null : record(item.slowest, 'slowest probe')

  return {
    host: text(item.host, 'host'),
    status: item.status == null ? null : oneOf(healthStatuses, item.status, 'health status'),
    probedAt: optionalText(item.probedAt, 'probe time'),
    totals: {
      healthy: count(totals.healthy, 'healthy total'),
      degraded: count(totals.degraded, 'degraded total'),
      unhealthy: count(totals.unhealthy, 'unhealthy total'),
      notProbed: count(totals.notProbed, 'not probed total'),
    },
    liveness: liveness && {
      healthy: count(liveness.healthy, 'healthy liveness'),
      total: count(liveness.total, 'liveness total'),
    },
    slowest: slowest && {
      name: text(slowest.name, 'slowest check'),
      durationMilliseconds: count(slowest.durationMilliseconds, 'slowest duration'),
    },
  }
}

async function read<T>(api: DashboardApi, path: string, parse: (value: unknown) => T, query?: Record<string, string>) {
  return parse(await api.request<unknown>({ method: 'GET', path, query }))
}

export const queryKeys = {
  summary: ['cluster', 'summary'],
  nodes: ['cluster', 'nodes'],
  node: (id: string) => ['cluster', 'nodes', 'one', id],
  health: ['cluster', 'health'],
  check: (name: string) => ['cluster', 'health', 'checks', 'one', name],
} as const

export function useSummary() {
  const { api } = useDashboard()

  return useQuery({ queryKey: queryKeys.summary, queryFn: () => read(api, 'cluster/summary', readSummary) })
}

/** The incarnations in the given state, or all; refetched every heartbeat while the view is open (`interval`). */
export function useNodes(state: NodeState | undefined, enabled: boolean, interval: number | false) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: [...queryKeys.nodes, state ?? 'all'],
    queryFn: () =>
      read(
        api,
        'cluster/nodes',
        (value) => list(record(value, 'node list').items, 'nodes', readNode),
        state ? { state } : undefined,
      ),
    enabled,
    refetchInterval: interval,
  })
}

export function useNodeDetail(id: string | undefined) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: queryKeys.node(id ?? ''),
    queryFn: () => read(api, `cluster/nodes/${encodeURIComponent(id ?? '')}`, readNodeDetail),
    enabled: id !== undefined,
    retry: false,
  })
}

export function useMetadataAgreement(enabled: boolean) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: [...queryKeys.nodes, 'agreement'],
    queryFn: () =>
      read(api, 'cluster/metadata-agreement', (value) =>
        list(record(value, 'agreement').keys, 'agreement keys', (entry) => {
          const key = record(entry, 'agreement key')

          return {
            key: text(key.key, 'agreement key'),
            isUnanimous: flag(key.isUnanimous, 'agreement flag'),
            groups: list(key.groups, 'agreement groups', readGroup),
          }
        }),
      ),
    enabled,
  })
}

export function useEventLog(enabled: boolean) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: [...queryKeys.nodes, 'events'],
    queryFn: () => read(api, 'cluster/events', readEventLog),
    enabled,
  })
}

export function useHostHealth(enabled: boolean) {
  const { api } = useDashboard()

  return useQuery({ queryKey: queryKeys.health, queryFn: () => read(api, 'cluster/health', readHealth), enabled })
}

export function useChecks(status: CheckFilter | undefined, enabled: boolean) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: [...queryKeys.health, 'checks', status ?? 'all'],
    queryFn: () =>
      read(
        api,
        'cluster/health/checks',
        (value) => list(record(value, 'check list').items, 'checks', readCheck),
        status ? { status } : undefined,
      ),
    enabled,
  })
}

export function useCheckDetail(name: string | undefined) {
  const { api } = useDashboard()

  return useQuery({
    queryKey: queryKeys.check(name ?? ''),
    queryFn: () => read(api, `cluster/health/checks/${encodeURIComponent(name ?? '')}`, readCheckDetail),
    enabled: name !== undefined,
    retry: false,
  })
}

/**
 * The module's commands as the shell describes them, for the current user: a viewer's copy cannot run and says why,
 * so a disabled action names the missing permission.
 */
export function useClusterCommands() {
  const { api } = useDashboard()
  const module = useModule()
  const commands = useQuery({
    queryKey: ['commands', module.id],
    queryFn: () => createCommandsApi(api, module.id).list(),
    staleTime: Infinity,
  })
  const all = commands.data ?? []

  function find(id: 'cluster.probe' | 'cluster.probe-all'): CommandDefinition | undefined {
    const command = all.find((candidate) => candidate.id === id)

    if (!command || module.canOperate) {
      return command
    }

    return { ...command, canRun: false, disabledReason: command.disabledReason ?? viewOnlyReason }
  }

  return { all, find }
}

const nodeTones: Readonly<Record<NodeState, DashboardStateRole>> = { Alive: 'ok', Suspected: 'proc', Dead: 'fail' }
const healthTones: Readonly<Record<HealthStatus, DashboardStateRole>> = {
  Healthy: 'ok',
  Degraded: 'proc',
  Unhealthy: 'fail',
}

export function nodeMark(state: NodeState): { tone: DashboardStateRole; label: string } {
  return { tone: nodeTones[state], label: state }
}

export function healthMark(status: HealthStatus): { tone: DashboardStateRole; label: string } {
  return { tone: healthTones[status], label: status }
}

/** The search parameter value as one of `values`, or `undefined` for none or a value the view does not serve. */
export function parseFilter<const T extends readonly string[]>(values: T, value: string | null): T[number] | undefined {
  return values.find((candidate) => candidate === value)
}
