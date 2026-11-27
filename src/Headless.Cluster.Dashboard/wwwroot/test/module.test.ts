import { describe, expect, it } from 'vitest'
import cluster from '../src/module.ts'

describe('cluster module definition', () => {
  it('serves the cluster product under the cluster id', () => {
    expect(cluster.id).toBe('cluster')
    expect(cluster.product).toBe('cluster')
  })

  it('gives every sidebar entry exactly one of an icon or a state dot', () => {
    const entries: readonly object[] = cluster.sidebar.flatMap((section): readonly object[] => section.entries)

    expect(entries.length).toBeGreaterThan(0)
    for (const entry of entries) {
      expect(['icon' in entry, 'state' in entry].filter(Boolean)).toHaveLength(1)
    }
  })

  it('loads each view as its own lazy chunk', () => {
    expect(cluster.routes.map((route) => route.path)).toEqual(['', 'health'])
    for (const route of cluster.routes) {
      expect(typeof route.lazy).toBe('function')
    }
  })

  it('opens a node in the cluster view and a check in the health view', () => {
    expect(cluster.objectKinds.node.route('orders-api-0@14')).toBe('?open=orders-api-0%4014')
    expect(cluster.objectKinds.check.route('sandbox-dependency')).toBe('health?open=sandbox-dependency')
    expect(cluster.objectKinds.health.route()).toBe('health')
  })
})
