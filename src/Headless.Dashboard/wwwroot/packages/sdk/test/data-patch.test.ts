import { QueryObserver, type QueryClient } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { bindStreamToClient } from '../src/data/bind.ts'
import { createDashboardClient, listKey, objectKey } from '../src/data/client.tsx'
import { capCount, formatCount } from '../src/data/counts.ts'
import { applyChange, compareVersions, getNewRows, showNewRows, type DashboardListPage } from '../src/data/patch.ts'
import { createFakeStream } from '../src/testing/fake-stream.ts'

interface Run {
  id: string
  version?: string
  status: string
}

function change(id: string, version: string, projection?: Record<string, unknown> | null) {
  return { module: 'diag', kind: 'run', id, topic: 'diag.run', version, projection }
}

describe('data patching', () => {
  let client: QueryClient
  const stops: (() => void)[] = []

  /** Mounts an observer, as a view would, so the query is active and refetches when invalidated. */
  async function watch(key: readonly unknown[], fetch: () => Promise<unknown>) {
    const observer = new QueryObserver(client, { queryKey: key, queryFn: fetch })
    stops.push(observer.subscribe(() => undefined))
    await vi.waitFor(() => expect(client.getQueryData(key)).toBeDefined())
  }

  beforeEach(() => {
    client = createDashboardClient()
  })

  afterEach(() => {
    stops.splice(0).forEach((stop) => stop())
    client.clear()
  })

  it('patches a list row and the cached object in place, without refetching the list', async () => {
    const fetchList = vi.fn(async (): Promise<DashboardListPage<Run>> => ({
      items: [
        { id: 'r1', version: '1', status: 'Queued' },
        { id: 'r2', version: '1', status: 'Queued' },
      ],
    }))
    await watch(listKey('diag.run', { status: 'all' }), fetchList)
    client.setQueryData(objectKey('diag.run', 'r1'), { id: 'r1', version: '1', status: 'Queued' })

    expect(applyChange(client, change('r1', '2', { status: 'Failed' }))).toBe('patched')

    const page = client.getQueryData<DashboardListPage<Run>>(
      listKey('diag.run', { status: 'all' }),
    )
    expect(page?.items).toEqual([
      { id: 'r1', version: '2', status: 'Failed' },
      { id: 'r2', version: '1', status: 'Queued' },
    ])
    expect(client.getQueryData(objectKey('diag.run', 'r1'))).toEqual({ id: 'r1', version: '2', status: 'Failed' })
    expect(fetchList).toHaveBeenCalledTimes(1)
  })

  it('ignores an event whose version is older than the cached object', () => {
    client.setQueryData(objectKey('diag.run', 'r1'), { id: 'r1', version: '10', status: 'Failed' })

    expect(applyChange(client, change('r1', '9', { status: 'Queued' }))).toBe('ignored')
    expect(applyChange(client, change('r1', '10', { status: 'Queued' }))).toBe('ignored')
    expect(client.getQueryData(objectKey('diag.run', 'r1'))).toEqual({ id: 'r1', version: '10', status: 'Failed' })
  })

  it('orders numeric versions of any length as numbers', () => {
    expect(compareVersions('10', '9')).toBe(1)
    expect(compareVersions('18446744073709551617', '18446744073709551616')).toBe(1)
    expect(compareVersions('2026-11-05T10:00:00.5Z', '2026-11-05T10:00:00.4Z')).toBe(1)
  })

  it('refetches the object when a change carries no projection', async () => {
    const fetchObject = vi.fn(async () => ({ id: 'r1', version: '1', status: 'Queued' }))
    await watch(objectKey('diag.run', 'r1'), fetchObject)

    expect(applyChange(client, change('r1', '2', null))).toBe('refetch')
    await vi.waitFor(() => expect(fetchObject).toHaveBeenCalledTimes(2))
  })

  it('counts a change no loaded list holds as a new row, and shows it on request', async () => {
    const fetchList = vi.fn(async (): Promise<DashboardListPage> => ({ items: [] }))
    await watch(listKey('diag.run'), fetchList)

    expect(applyChange(client, change('r9', '1', { status: 'Failed' }))).toBe('new')
    expect(applyChange(client, change('r9', '2', { status: 'Failed' }))).toBe('new')
    expect(getNewRows(client, 'diag.run').size).toBe(1)

    await showNewRows(client, 'diag.run')

    expect(getNewRows(client, 'diag.run').size).toBe(0)
    expect(fetchList).toHaveBeenCalledTimes(2)
  })

  it('refetches only the queries of newly added topics after subscribed', async () => {
    const stream = createFakeStream()
    stops.push(bindStreamToClient(stream, client))
    const runs = vi.fn(async () => ({ items: [] }))
    const nodes = vi.fn(async () => ({ items: [] }))
    await watch(listKey('diag.run'), runs)
    await watch(listKey('diag.node'), nodes)

    stream.push('subscribed', { topics: ['diag.run', 'diag.node'], added: ['diag.node'] })

    await vi.waitFor(() => expect(nodes).toHaveBeenCalledTimes(2))
    expect(runs).toHaveBeenCalledTimes(1)
  })

  it('refetches every subscribed query on reset', async () => {
    const stream = createFakeStream()
    stops.push(bindStreamToClient(stream, client))
    const runs = vi.fn(async () => ({ items: [] }))
    const run = vi.fn(async () => ({ id: 'r1' }))
    await watch(listKey('diag.run'), runs)
    await watch(objectKey('diag.run', 'r1'), run)

    stream.push('reset', {})

    await vi.waitFor(() => expect(runs).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(run).toHaveBeenCalledTimes(2))
  })

  it('formats a capped count with a plus', () => {
    expect(formatCount(capCount(12_345, 10_000), 'en')).toBe('10,000+')
    expect(formatCount(capCount(312, 10_000), 'en')).toBe('312')
  })
})
