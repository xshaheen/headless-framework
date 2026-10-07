import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'

const row = (id: string, status: string, children: unknown[] = []) => ({
  id,
  function: `Fn${id}`,
  status,
  retries: 0,
  retryCount: 0,
  retryIntervals: [],
  description: '',
  requestType: `Req${id}`,
  lockHolder: '-',
  lockedAt: '',
  executionTime: '2026-01-01T00:00:00Z',
  executionTimeFormatted: '',
  createdAt: '',
  updatedAt: '',
  executedAt: '',
  elapsedTime: 0,
  children,
})

const rows = [
  row('failed-1', 'Failed'),
  row('idle-1', 'Idle'),
  row('running-1', 'InProgress'),
  row('parent-1', 'Done', [row('child-1', 'Idle')]),
  { ...row('untyped-1', 'Done'), requestType: '' },
]

const listService = fakeService({ items: rows, totalCount: rows.length, pageNumber: 1, pageSize: 20 })
// The server refuses the cancel, which is what offers a force delete on that row.
const cancelService = fakeService<object>({}, async () => {
  throw new Error('NotRunningHere')
})

vi.mock('@/http/services/timeJobService', () => ({
  timeJobService: {
    getTimeJobsPaginated: () => listService,
    deleteTimeJob: () => fakeService({}),
    deleteTimeJobsBatch: () => fakeService({}),
    getTimeJobsGraphDataRange: () => fakeService([]),
    getTimeJobsGraphData: () => fakeService([]),
  },
}))

vi.mock('@/http/services/jobsService', () => ({
  jobsService: {
    requestCancel: () => cancelService,
    requeue: () => fakeService({}),
  },
}))

vi.mock('@/stores/connectionStore', () => ({
  useConnectionStore: () => ({ isInitialized: true }),
}))

vi.mock('@/hub/jobNotificationHub', () => {
  const noop = () => {}
  return {
    methodName: new Proxy({}, { get: (_, key) => String(key) }),
    default: {
      onReceiveAddTimeJob: noop,
      onReceiveUpdateTimeJob: noop,
      onReceiveDeleteTimeJob: noop,
      onReceiveAddTimeJobsBatch: noop,
      onReceiveJobProgress: noop,
      stopReceiver: noop,
    },
  }
})

vi.mock('vue-echarts', () => ({ default: { name: 'VChart', render: () => null }, THEME_KEY: 'ecTheme' }))

vi.mock('@/components/ChainJobsModal.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
// Renders which job it was opened for, so the request-type button can be shown to open it.
vi.mock('@/components/common/JobRequestDialog.vue', async () => {
  const { h } = await import('vue')
  return {
    __esModule: true,
    default: {
      props: ['dialogProps', 'isOpen'],
      setup: (props: { dialogProps: { id?: string }; isOpen: boolean }) => () =>
        props.isOpen ? h('div', { class: 'request-dialog-stub' }, props.dialogProps.id) : null,
    },
  }
})
vi.mock('@/components/timeJobComponents/CRUDTimeJobDialogComponent.vue', () => ({
  __esModule: true,
  default: { render: () => null },
}))

const TimeJob = (await import('@/views/TimeJob.vue')).default

const rowOf = (id: string) =>
  [...document.querySelectorAll<HTMLTableRowElement>('tbody tr')].find((tr) => tr.textContent?.includes(`Fn${id}`))

const labelOf = (id: string, selector: string) => rowOf(id)?.querySelector(selector)?.getAttribute('aria-label')

describe('TimeJob accessible names', () => {
  let mounted: Mounted

  beforeEach(async () => {
    mounted = mountWithApp(TimeJob)
    await flush()
  })

  afterEach(() => mounted.unmount())

  it('names every icon-only row action', () => {
    expect(labelOf('running-1', '.cancel-btn')).toBe('Cancel job')
    expect(labelOf('failed-1', '.requeue-btn')).toBe('Requeue job')
    expect(labelOf('idle-1', '.edit-btn')).toBe('Edit job')
    expect(labelOf('failed-1', '.duplicate-btn')).toBe('Duplicate job')
    expect(labelOf('failed-1', '.delete-btn')).toBe('Delete job')
    expect(document.querySelector('.refresh-btn')?.getAttribute('aria-label')).toBe('Refresh')
  })

  it('renames delete to force delete once the cancel was refused', async () => {
    rowOf('running-1')!.querySelector<HTMLButtonElement>('.cancel-btn')!.click()
    await flush()

    expect(labelOf('running-1', '.delete-btn')).toBe('Force delete job')
    expect(labelOf('failed-1', '.delete-btn')).toBe('Delete job')
  })

  it('names the tree toggle by what a click does and reports its state', async () => {
    const toggle = () => rowOf('parent-1')!.querySelector<HTMLButtonElement>('.tree-expand-btn')!

    expect(toggle().getAttribute('aria-label')).toBe('Expand children')
    expect(toggle().getAttribute('aria-expanded')).toBe('false')

    toggle().click()
    await flush()

    expect(toggle().getAttribute('aria-label')).toBe('Collapse children')
    expect(toggle().getAttribute('aria-expanded')).toBe('true')
  })

  it('makes the request type a keyboard-reachable button named for its payload', async () => {
    const link = rowOf('failed-1')!.querySelector<HTMLElement>('.blue-underline')!

    expect(link.tagName).toBe('BUTTON')
    expect(link.getAttribute('type')).toBe('button')
    expect(link.getAttribute('aria-label')).toBe('View request payload for Reqfailed-1')

    link.click()
    await flush()
    expect(document.querySelector('.request-dialog-stub')?.textContent).toBe('failed-1')
  })

  // A job registered without a request type has no payload to show; an empty button would be an invisible tab stop
  // named only "View request payload for ".
  it('renders no request-type button for a job without a request type', () => {
    expect(rowOf('untyped-1')!.querySelector('.blue-underline')).toBeNull()
  })
})
