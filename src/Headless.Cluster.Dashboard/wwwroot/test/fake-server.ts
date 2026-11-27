import type { CommandDefinition } from '@headless/dashboard-sdk/commands'
import { DashboardApiError, type ApiRequest } from '@headless/dashboard-sdk/data'
import { renderModule, type RenderModuleOptions } from '@headless/dashboard-sdk/testing'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type {
  ClusterNode,
  ClusterSummary,
  HealthCheck,
  HostHealth,
  MembershipEvent,
  NodeAgreement,
  ProbeResult,
} from '../src/api.ts'
import cluster from '../src/module.ts'

export const now = new Date('2026-11-27T10:00:00Z')

export function node(overrides: Partial<ClusterNode> & Pick<ClusterNode, 'id'>): ClusterNode {
  const [nodeId = overrides.id, incarnation = '1'] = overrides.id.split('@')

  return {
    nodeId,
    incarnation: Number(incarnation),
    state: 'Alive',
    role: 'worker',
    lastHeartbeatAt: '2026-11-27T09:59:55Z',
    isLocal: false,
    ownerTag: `orders/${overrides.id}`,
    metadata: {},
    ...overrides,
  }
}

export function summary(overrides: Partial<ClusterSummary> = {}): ClusterSummary {
  return {
    host: 'orders-api-0@14',
    coordination: {
      clusterName: 'orders',
      providerName: 'PostgreSql',
      role: 'api',
      localNode: 'orders-api-0@14',
      heartbeatIntervalMilliseconds: 5000,
      suspicionThresholdMilliseconds: 15000,
      deadThresholdMilliseconds: 30000,
      deadRetentionWindowMilliseconds: 30000,
      selfFenceMarginMilliseconds: 5000,
      membershipHeartbeatEnabled: true,
    },
    health: { registered: true, monitor: true },
    ...overrides,
  }
}

export function result(status: ProbeResult['status'], overrides: Partial<ProbeResult> = {}): ProbeResult {
  return {
    status,
    durationMilliseconds: 3,
    description: `${status} answer`,
    completedAt: '2026-11-27T09:59:00Z',
    ...overrides,
  }
}

export function check(name: string, overrides: Partial<HealthCheck> = {}): HealthCheck {
  return {
    id: name,
    name,
    tags: ['ready', 'headless'],
    failureStatus: 'Unhealthy',
    timeoutMilliseconds: 5000,
    isLiveness: false,
    latest: null,
    ...overrides,
  }
}

/** The host's health as the server derives it from the checks' latest results. */
export function healthSummary(checks: readonly HealthCheck[], host = 'orders-api-0@14'): HostHealth {
  const latest = checks.flatMap((item) => (item.latest ? [item.latest] : []))
  const order = ['Unhealthy', 'Degraded', 'Healthy'] as const
  const liveness = checks.filter((item) => item.isLiveness)
  const slowest = checks
    .filter((item) => item.latest)
    .sort((a, b) => (b.latest?.durationMilliseconds ?? 0) - (a.latest?.durationMilliseconds ?? 0))[0]

  return {
    host,
    status: order.find((status) => latest.some((item) => item.status === status)) ?? null,
    probedAt:
      latest
        .map((item) => item.completedAt)
        .sort()
        .at(-1) ?? null,
    totals: {
      healthy: latest.filter((item) => item.status === 'Healthy').length,
      degraded: latest.filter((item) => item.status === 'Degraded').length,
      unhealthy: latest.filter((item) => item.status === 'Unhealthy').length,
      notProbed: checks.length - latest.length,
    },
    liveness: liveness.length
      ? { healthy: liveness.filter((item) => item.latest?.status === 'Healthy').length, total: liveness.length }
      : null,
    slowest: slowest?.latest ? { name: slowest.name, durationMilliseconds: slowest.latest.durationMilliseconds } : null,
  }
}

const base = {
  permission: 'operate' as const,
  typedConfirmationAbove: 100,
  canRun: true,
  confirmation: 'none' as const,
  requiresReason: false,
  supportsFilter: false,
  arguments: { type: 'object' as const, required: [], properties: {} },
}

export const commands: CommandDefinition[] = [
  { ...base, id: 'cluster.probe', label: 'Probe now', objectKind: 'check' },
  { ...base, id: 'cluster.probe-all', label: 'Probe all', objectKind: 'health' },
]

export interface FakeServer {
  summary?: ClusterSummary
  nodes?: ClusterNode[]
  /** Per node id, its agreement and events; nodes without an entry answer with none. */
  details?: Readonly<Record<string, { agreement?: NodeAgreement[]; events?: MembershipEvent[] }>>
  /** Node ids whose detail read answers 404, as for a node that left the membership store. */
  goneNodes?: readonly string[]
  agreement?: { key: string; isUnanimous: boolean; groups: { value: string | null; nodes: string[] }[] }[]
  checks?: HealthCheck[]
  /** Per check name, up to 30 results newest first; the check's latest only by default. */
  history?: Readonly<Record<string, ProbeResult[]>>
  /**
   * The sidebar counts by key, as `GET api/counts?product=cluster` answers them; an object names the sources a count
   * left out.
   */
  counts?: Readonly<Record<string, number | { count: number; unavailable: string[] }>>
  /** Why the server refuses the probe commands now; allowed by default. */
  disabledReason?: string
  /** What the server does when a probe command runs; answers `Done` per id by default. */
  onCommand?: (commandId: string, ids: readonly string[]) => unknown
  extra?: (request: ApiRequest) => unknown
}

/** A fake of the Cluster module's API, the shell's aggregates it reads, and the shell's command endpoints for it. */
export function server(fake: FakeServer) {
  return (request: ApiRequest) => {
    const answered = fake.extra?.(request)

    if (answered !== undefined) {
      return answered
    }

    const query = request.query ?? {}
    const nodes = fake.nodes ?? []
    const checks = fake.checks ?? []

    if (request.path === 'counts') {
      return {
        counts: Object.fromEntries(
          Object.entries(fake.counts ?? {}).map(([key, count]) => [
            key,
            typeof count === 'number' ? { count, capped: false } : { ...count, capped: false },
          ]),
        ),
        stale: [],
      }
    }

    if (request.path === 'cluster/summary') {
      return fake.summary ?? summary()
    }

    if (request.path === 'cluster/nodes') {
      return { items: nodes.filter((item) => !query.state || item.state === query.state) }
    }

    if (request.path === 'cluster/metadata-agreement') {
      return { keys: fake.agreement ?? [] }
    }

    const one = /^cluster\/nodes\/(.+)$/.exec(request.path)

    if (one) {
      const id = decodeURIComponent(one[1]!)
      const found = nodes.find((item) => item.id === id)

      if (!found || fake.goneNodes?.includes(id)) {
        throw new DashboardApiError(404, 'Not found')
      }

      return { node: found, agreement: fake.details?.[id]?.agreement ?? [], events: fake.details?.[id]?.events ?? [] }
    }

    if (request.path === 'cluster/health') {
      return healthSummary(checks, (fake.summary ?? summary()).host)
    }

    if (request.path === 'cluster/health/checks') {
      const status = query.status

      return {
        items: checks.filter(
          (item) => !status || (status === 'NotProbed' ? item.latest === null : item.latest?.status === status),
        ),
      }
    }

    const named = /^cluster\/health\/checks\/(.+)$/.exec(request.path)

    if (named) {
      const name = decodeURIComponent(named[1]!)
      const found = checks.find((item) => item.name.toLowerCase() === name.toLowerCase())

      if (!found) {
        throw new DashboardApiError(404, 'Not found')
      }

      return { check: found, results: fake.history?.[found.name] ?? (found.latest ? [found.latest] : []) }
    }

    if (request.path === 'cluster/commands') {
      return { commands }
    }

    if (request.path === 'cluster/commands/allowed') {
      const ids = [query.id ?? []].flat()

      return {
        objects: ids.map((id) => ({
          kind: query.kind,
          id,
          commands: commands.map((command) => ({
            id: command.id,
            allowed: !fake.disabledReason,
            disabledReason: fake.disabledReason,
          })),
        })),
      }
    }

    const command = /^cluster\/commands\/([^/]+)$/.exec(request.path)

    if (request.method === 'POST' && command) {
      const ids = (request.body as { ids: string[] }).ids

      return fake.onCommand?.(command[1]!, ids) ?? { rows: ids.map((id) => ({ id, outcome: 'done' })) }
    }

    throw new DashboardApiError(404, `No fake for ${request.method} ${request.path}`)
  }
}

export function open(fake: FakeServer, options: RenderModuleOptions = {}) {
  return renderModule(cluster, { api: server(fake), ...options })
}

export function nodeChange(id: string, version = '2') {
  return { module: 'cluster', kind: 'node', id, topic: 'cluster.node', version }
}

export function checkChange(name: string, version = '2') {
  return { module: 'cluster', kind: 'check', id: name, topic: 'cluster.check', version }
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
}
