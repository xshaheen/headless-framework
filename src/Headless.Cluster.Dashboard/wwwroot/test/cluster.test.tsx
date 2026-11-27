import { act, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { restoreRealTimers } from '@headless/dashboard-sdk/testing/timers'
import { node, nodeChange, now, open, summary, user, type FakeServer } from './fake-server.ts'

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.setSystemTime(now)
})

afterEach(async () => {
  await restoreRealTimers()
  localStorage.clear()
})

const nodes = [
  node({ id: 'orders-api-0@14', role: 'api', isLocal: true, metadata: { 'app.version': '4.2.1' } }),
  node({ id: 'orders-api-1@2', role: 'api', metadata: { 'app.version': '4.2.1' } }),
  node({ id: 'orders-worker-0@7', metadata: { 'app.version': '4.2.1' } }),
  node({ id: 'orders-worker-1@5', state: 'Suspected', metadata: { 'app.version': '4.2.0' } }),
  node({ id: 'orders-worker-2@3', state: 'Dead', lastHeartbeatAt: '2026-11-27T09:58:00Z' }),
]

const fake: FakeServer = {
  nodes,
  agreement: [
    {
      key: 'app.version',
      isUnanimous: false,
      groups: [
        { value: '4.2.1', nodes: ['orders-api-0@14', 'orders-api-1@2', 'orders-worker-0@7'] },
        { value: '4.2.0', nodes: ['orders-worker-1@5'] },
      ],
    },
  ],
  details: {
    'orders-worker-1@5': {
      agreement: [
        {
          key: 'app.version',
          isUnanimous: false,
          value: '4.2.0',
          matches: 1,
          compared: 4,
          groups: [{ value: '4.2.1', nodes: ['orders-api-0@14', 'orders-api-1@2', 'orders-worker-0@7'] }],
        },
        {
          key: 'pod',
          isUnanimous: false,
          value: 'w1',
          matches: 1,
          compared: 3,
          groups: [
            { value: 'a0', nodes: ['orders-api-0@14'] },
            { value: 'a1', nodes: ['orders-api-1@2'] },
          ],
        },
      ],
      events: [
        {
          sequence: 3,
          kind: 'NodeSuspected',
          node: 'orders-worker-1@5',
          observedAt: '2026-11-27T09:59:40Z',
          state: 'Suspected',
          lastHeartbeatAt: '2026-11-27T09:59:20Z',
          isFirstObservation: false,
        },
        {
          sequence: 1,
          kind: 'NodeJoined',
          node: 'orders-worker-1@5',
          observedAt: '2026-11-27T09:50:00Z',
          state: 'Alive',
          lastHeartbeatAt: '2026-11-27T09:50:00Z',
          isFirstObservation: true,
        },
      ],
    },
  },
}

function reads(view: ReturnType<typeof open>, path: string) {
  return view.requests.filter((request) => request.path === path).length
}

describe('cluster nodes', () => {
  it('lists every incarnation with its role, state word, and last heartbeat, and no column the server does not serve', async () => {
    open(fake)

    const grid = await screen.findByRole('grid', { name: 'Nodes' })
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['node', 'role', 'state', 'last heartbeat'])
    expect(within(grid).getAllByRole('row')).toHaveLength(6)
    expect(screen.getByRole('row', { name: /orders-api-0@14/ }).textContent).toContain('This node')
    expect(screen.getByRole('row', { name: /orders-worker-1@5/ }).textContent).toContain('Suspected')
    expect(screen.getByRole('row', { name: /orders-worker-2@3/ }).textContent).toContain('Dead')
    expect(document.body.textContent).not.toMatch(/\bLeft\b/)
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('shows the metadata that differs across the live nodes on the page', async () => {
    open(fake)

    const section = await screen.findByRole('region', { name: 'Metadata agreement' })
    expect(section.textContent).toContain('app.version')
    expect(section.textContent).toContain('4.2.1 on 3 nodes · 4.2.0 on 1 node')
  })

  it('reads only the nodes in the state the URL filters by', async () => {
    const view = open({ ...fake, nodes: nodes.filter((item) => item.state === 'Dead') }, { path: '?state=Dead' })

    await screen.findByRole('row', { name: /orders-worker-2@3/ })
    expect(view.requests.some((request) => request.path === 'cluster/nodes' && request.query?.state === 'Dead')).toBe(
      true,
    )
  })

  it('opens a node with its metadata agreement and its events seen by this node', async () => {
    const actor = user()
    open(fake)

    await actor.click(await screen.findByRole('row', { name: /orders-worker-1@5/ }))
    const panel = await screen.findByRole('region', { name: 'Node orders-worker-1@5' })

    await actor.click(within(panel).getByRole('tab', { name: 'Metadata' }))
    expect(panel.textContent).toContain('differs')
    expect(panel.textContent).toContain('app.version: Matches on 1 of 4 live nodes')
    expect(panel.textContent).toContain('pod: Differs on every node')
    expect(panel.textContent).not.toMatch(/differs pod/)

    await actor.click(within(panel).getByRole('tab', { name: 'Events' }))
    expect(panel.textContent).toContain('Seen by orders-api-0@14, local clock')
    expect(panel.textContent).toContain('Alive (starting state)')
  })

  it('flags a two-node version skew as differing', async () => {
    const actor = user()
    open({
      ...fake,
      details: {
        'orders-api-1@2': {
          agreement: [
            {
              key: 'app.version',
              isUnanimous: false,
              value: '4.2.1',
              matches: 1,
              compared: 2,
              groups: [{ value: '4.2.0', nodes: ['orders-worker-1@5'] }],
            },
          ],
        },
      },
    })

    await actor.click(await screen.findByRole('row', { name: /orders-api-1@2/ }))
    const panel = await screen.findByRole('region', { name: 'Node orders-api-1@2' })
    await actor.click(within(panel).getByRole('tab', { name: 'Metadata' }))
    expect(panel.textContent).toContain('differs app.version: Matches on 1 of 2 live nodes')
  })

  it('explains a node that left the membership store instead of drawing a blank panel', async () => {
    open({ ...fake, goneNodes: ['orders-worker-2@3'] }, { path: '?open=orders-worker-2%403' })

    const panel = await screen.findByRole('region', { name: 'Node orders-worker-2@3' })
    await waitFor(() => expect(panel.textContent).toContain('This node is no longer in the membership store.'))
  })

  it('reads the nodes and the open panel again on a node change', async () => {
    const view = open(fake, { path: '?open=orders-worker-1%405' })
    await screen.findByRole('region', { name: 'Node orders-worker-1@5' })
    await screen.findByRole('row', { name: /orders-api-1@2/ })
    const list = reads(view, 'cluster/nodes')
    const panel = reads(view, 'cluster/nodes/orders-worker-1%405')

    act(() => view.stream.push('change', nodeChange('orders-worker-1@5')))

    await waitFor(() => expect(reads(view, 'cluster/nodes')).toBe(list + 1))
    await waitFor(() => expect(reads(view, 'cluster/nodes/orders-worker-1%405')).toBe(panel + 1))
  })

  it('reads the nodes again on a stream reset', async () => {
    const view = open(fake)
    await screen.findByRole('grid', { name: 'Nodes' })
    const before = reads(view, 'cluster/nodes')

    act(() => view.stream.push('reset', {}))

    await waitFor(() => expect(reads(view, 'cluster/nodes')).toBe(before + 1))
  })

  it('reads the nodes again every heartbeat so their last heartbeat stays current', async () => {
    const view = open(fake)
    await screen.findByRole('grid', { name: 'Nodes' })
    const before = reads(view, 'cluster/nodes')

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
    })

    await waitFor(() => expect(reads(view, 'cluster/nodes')).toBe(before + 1))
  })

  it('keeps an open node that left the filter in the panel and marks its row', async () => {
    let suspected = true
    const shifting = node({ id: 'orders-worker-1@5', state: 'Suspected' })
    const view = open(
      {
        ...fake,
        extra: (request) =>
          request.path === 'cluster/nodes'
            ? { items: suspected ? [shifting] : [] }
            : request.path === 'cluster/nodes/orders-worker-1%405'
              ? { node: { ...shifting, state: suspected ? 'Suspected' : 'Alive' }, agreement: [], events: [] }
              : undefined,
      },
      { path: '?state=Suspected&open=orders-worker-1%405' },
    )
    await screen.findByRole('row', { name: /orders-worker-1@5/ })

    suspected = false
    act(() => view.stream.push('change', nodeChange('orders-worker-1@5')))

    await waitFor(() =>
      expect(screen.getByRole('row', { name: /orders-worker-1@5/ }).textContent).toContain(
        'No longer matches the filter',
      ),
    )
    expect(screen.getByRole('region', { name: 'Node orders-worker-1@5' })).toBeTruthy()
  })

  it('says this node is not in the membership store when the snapshot lacks it', async () => {
    open({ ...fake, nodes: nodes.map((item) => ({ ...item, isLocal: false })) })

    expect(await screen.findByText(/This node is not in the membership store/)).toBeTruthy()
    expect(screen.queryByText('This node')).toBeNull()
  })

  it('explains that Coordination is not registered and reads no nodes on a host without it', async () => {
    const view = open({ ...fake, summary: summary({ coordination: null }) })

    expect(await screen.findByText('Coordination is not registered')).toBeTruthy()
    expect(document.body.textContent).toContain('AddHeadlessCoordination')
    expect(screen.queryByRole('grid')).toBeNull()
    expect(reads(view, 'cluster/nodes')).toBe(0)
  })

  it('shows a failed first read as an error with Retry, not as an empty list', async () => {
    open({
      ...fake,
      extra: (request) => {
        if (request.path === 'cluster/nodes') {
          throw new Error('The membership store did not answer.')
        }

        return undefined
      },
    })

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByRole('button', { name: /Retry/ })).toBeTruthy()
    expect(screen.queryByText('No nodes')).toBeNull()
  })
})
