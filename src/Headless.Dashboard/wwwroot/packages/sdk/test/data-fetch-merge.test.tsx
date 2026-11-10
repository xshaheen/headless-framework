import { renderHook, waitFor } from '@testing-library/react'
import type { QueryClient } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DashboardProvider } from '../src/context.tsx'
import { createDashboardClient, listKey, objectKey } from '../src/data/client.tsx'
import { useDashboardList, useDashboardObject, useNewRows } from '../src/data/hooks.ts'
import { useDashboardKeysetList } from '../src/data/keyset.ts'
import type { DashboardListPage } from '../src/data/patch.ts'
import { createFakeStream, type FakeStream } from '../src/testing/fake-stream.ts'

interface Run {
  id: string
  version?: string
  status: string
}

function change(id: string, version: string, projection: Record<string, unknown> | null) {
  return { module: 'diag', kind: 'run', id, topic: 'diag.run', version, projection }
}

/** A request the test answers when it chooses, so a stream change can land while it is in flight. */
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })

  return { promise, resolve }
}

describe('fetch results against newer stream changes', () => {
  let client: QueryClient
  let stream: FakeStream

  function wrapper({ children }: { children: ReactNode }) {
    return (
      <DashboardProvider
        client={client}
        environment={{
          basePath: '/headless',
          apiBase: '/headless/api',
          session: { authenticated: true },
          permissions: {},
          stream,
          api: { request: vi.fn() },
        }}
      >
        {children}
      </DashboardProvider>
    )
  }

  beforeEach(() => {
    client = createDashboardClient()
    stream = createFakeStream()
  })

  it('keeps the newer object when a refetch started before a stream change lands after it', async () => {
    const answers = [deferred<Run>(), deferred<Run>()]
    let call = 0
    const { result } = renderHook(
      () => useDashboardObject<Run>({ topic: 'diag.run', id: 'r1', fetch: () => answers[call++]!.promise }),
      { wrapper },
    )
    answers[0]!.resolve({ id: 'r1', version: '5', status: 'Running' })
    await waitFor(() => expect(result.current.data?.version).toBe('5'))

    void client.invalidateQueries({ queryKey: objectKey('diag.run', 'r1') })
    await waitFor(() => expect(call).toBe(2))
    stream.push('change', change('r1', '6', { status: 'Succeeded' }))
    answers[1]!.resolve({ id: 'r1', version: '5', status: 'Running' })

    await waitFor(() => expect(result.current.isFetching).toBe(false))
    expect(result.current.data).toEqual({ id: 'r1', version: '6', status: 'Succeeded' })
  })

  it('keeps the newer list row when a refetch started before a stream change lands after it', async () => {
    const answers = [deferred<DashboardListPage<Run>>(), deferred<DashboardListPage<Run>>()]
    let call = 0
    const { result } = renderHook(
      () => useDashboardList<Run>({ topic: 'diag.run', fetch: () => answers[call++]!.promise }),
      { wrapper },
    )
    answers[0]!.resolve({ items: [{ id: 'r1', version: '5', status: 'Running' }] })
    await waitFor(() => expect(result.current.data?.items[0]?.version).toBe('5'))

    void client.invalidateQueries({ queryKey: listKey('diag.run') })
    await waitFor(() => expect(call).toBe(2))
    stream.push('change', change('r1', '6', { status: 'Succeeded' }))
    answers[1]!.resolve({
      items: [
        { id: 'r1', version: '5', status: 'Running' },
        { id: 'r2', version: '1', status: 'Queued' },
      ],
    })

    await waitFor(() => expect(result.current.isFetching).toBe(false))
    expect(result.current.data?.items).toEqual([
      { id: 'r1', version: '6', status: 'Succeeded' },
      { id: 'r2', version: '1', status: 'Queued' },
    ])
  })

  it('keeps the newer keyset row when a refetch started before a stream change lands after it', async () => {
    const answers = [deferred<DashboardListPage<Run>>(), deferred<DashboardListPage<Run>>()]
    let call = 0
    const { result } = renderHook(
      () => useDashboardKeysetList<Run>({ topic: 'diag.run', fetchPage: () => answers[call++]!.promise }),
      { wrapper },
    )
    answers[0]!.resolve({ items: [{ id: 'r1', version: '5', status: 'Running' }], nextCursor: null })
    await waitFor(() => expect(result.current.rows[0]?.version).toBe('5'))

    void client.invalidateQueries({ queryKey: listKey('diag.run') })
    await waitFor(() => expect(call).toBe(2))
    stream.push('change', change('r1', '6', { status: 'Succeeded' }))
    answers[1]!.resolve({ items: [{ id: 'r1', version: '5', status: 'Running' }], nextCursor: null })

    await waitFor(() => expect(result.current.query.isFetching).toBe(false))
    expect(result.current.rows).toEqual([{ id: 'r1', version: '6', status: 'Succeeded' }])
  })

  it('applies a change that arrived before the first fetch landed, and drops it from the new rows notice', async () => {
    const answer = deferred<DashboardListPage<Run>>()
    const { result } = renderHook(
      () => ({
        list: useDashboardList<Run>({ topic: 'diag.run', fetch: () => answer.promise }),
        newRows: useNewRows('diag.run').count,
      }),
      { wrapper },
    )

    stream.push('change', change('r1', '6', { status: 'Succeeded' }))
    await waitFor(() => expect(result.current.newRows).toBe(1))
    answer.resolve({ items: [{ id: 'r1', version: '5', status: 'Running' }] })

    await waitFor(() => expect(result.current.list.data).toBeDefined())
    expect(result.current.list.data?.items).toEqual([{ id: 'r1', version: '6', status: 'Succeeded' }])
    expect(result.current.newRows).toBe(0)
  })

  it('applies a change that arrived before an object first loaded', async () => {
    const answer = deferred<Run>()
    const { result } = renderHook(
      () => useDashboardObject<Run>({ topic: 'diag.run', id: 'r1', fetch: () => answer.promise }),
      { wrapper },
    )

    stream.push('change', change('r1', '6', { status: 'Succeeded' }))
    answer.resolve({ id: 'r1', version: '5', status: 'Running' })

    await waitFor(() => expect(result.current.data).toEqual({ id: 'r1', version: '6', status: 'Succeeded' }))
  })

  it('refetches a list whose first fetch landed older than a change that carried no projection', async () => {
    const answers = [deferred<DashboardListPage<Run>>(), deferred<DashboardListPage<Run>>()]
    let call = 0
    const { result } = renderHook(
      () => ({
        list: useDashboardList<Run>({ topic: 'diag.run', fetch: () => answers[call++]!.promise }),
        newRows: useNewRows('diag.run').count,
      }),
      { wrapper },
    )

    stream.push('change', change('r1', '6', null))
    await waitFor(() => expect(result.current.newRows).toBe(1))
    answers[0]!.resolve({ items: [{ id: 'r1', version: '5', status: 'Running' }] })

    await waitFor(() => expect(call).toBe(2))
    expect(result.current.newRows).toBe(1)
    answers[1]!.resolve({ items: [{ id: 'r1', version: '6', status: 'Succeeded' }] })

    await waitFor(() => expect(result.current.list.data?.items).toEqual([{ id: 'r1', version: '6', status: 'Succeeded' }]))
    expect(result.current.newRows).toBe(0)
  })

  it('refetches an object whose first fetch landed older than a change that carried no projection', async () => {
    const answers = [deferred<Run>(), deferred<Run>()]
    let call = 0
    const { result } = renderHook(
      () => useDashboardObject<Run>({ topic: 'diag.run', id: 'r1', fetch: () => answers[call++]!.promise }),
      { wrapper },
    )

    stream.push('change', change('r1', '6', null))
    answers[0]!.resolve({ id: 'r1', version: '5', status: 'Running' })

    await waitFor(() => expect(call).toBe(2))
    answers[1]!.resolve({ id: 'r1', version: '6', status: 'Succeeded' })

    await waitFor(() => expect(result.current.data).toEqual({ id: 'r1', version: '6', status: 'Succeeded' }))
  })

  it('asks once, not in a loop, when the refetch still answers the older row', async () => {
    let call = 0
    const { result } = renderHook(
      () =>
        useDashboardObject<Run>({
          topic: 'diag.run',
          id: 'r1',
          fetch: async () => {
            call++
            return { id: 'r1', version: '5', status: 'Running' }
          },
        }),
      { wrapper },
    )

    stream.push('change', change('r1', '6', null))

    await waitFor(() => expect(call).toBe(2))
    await waitFor(() => expect(result.current.isFetching).toBe(false))
    await new Promise((done) => setTimeout(done, 20))
    expect(call).toBe(2)
  })
})
