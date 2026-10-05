import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { deferred, fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'

const row = (id: string, status: string) => ({
  id,
  function: `Fn${id}`,
  status,
  retries: 0,
  retryCount: 0,
  retryIntervals: [],
  description: '',
  requestType: '',
  lockHolder: '-',
  lockedAt: '',
  executionTime: '2026-01-01T00:00:00Z',
  executionTimeFormatted: '',
  createdAt: '',
  updatedAt: '',
  executedAt: '',
  elapsedTime: 0,
  children: [],
})

const rows = [row('failed-1', 'Failed'), row('done-1', 'Done'), row('idle-1', 'Idle'), row('running-1', 'InProgress')]

let requeueGate = deferred()

const listService = fakeService({ items: rows, totalCount: rows.length, pageNumber: 1, pageSize: 20 })
const requeueService = fakeService<object>({}, () => requeueGate.promise)

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
    requestCancel: () => fakeService({}),
    requeue: () => requeueService,
  },
}))

vi.mock('@/stores/connectionStore', () => ({
  useConnectionStore: () => ({ isInitialized: true }),
}))

// The real module opens a SignalR connection on import, so the hub is replaced wholesale; method names only need to be
// distinct strings for stopReceiver.
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

// Charts render to canvas, which happy-dom does not implement; they are irrelevant to the row actions.
vi.mock('vue-echarts', () => ({ default: { name: 'VChart', render: () => null }, THEME_KEY: 'ecTheme' }))

// Child dialogs and modals pull their own services and stores; none of them takes part in the row actions. The
// __esModule flag lets defineAsyncComponent unwrap the default export from the lazily imported dialogs.
vi.mock('@/components/ChainJobsModal.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/JobRequestDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/timeJobComponents/CRUDTimeJobDialogComponent.vue', () => ({
  __esModule: true,
  default: { render: () => null },
}))

// Imported after the mocks are registered and the fakes above exist, because the mock factories close over them.
const TimeJob = (await import('@/views/TimeJob.vue')).default

const requeueButton = (id: string) =>
  [...document.querySelectorAll<HTMLTableRowElement>('tbody tr')]
    .find((tr) => tr.textContent?.includes(`Fn${id}`))
    ?.querySelector<HTMLButtonElement>('.requeue-btn') ??
  null

describe('TimeJob requeue action', () => {
  let mounted: Mounted

  beforeEach(async () => {
    requeueGate = deferred()
    listService.requestAsync.mockClear()
    requeueService.requestAsync.mockClear()
    mounted = mountWithApp(TimeJob)
    await flush()
    listService.requestAsync.mockClear()
  })

  afterEach(() => mounted.unmount())

  it('offers requeue only on Failed rows', () => {
    expect(document.querySelectorAll('tbody tr').length).toBeGreaterThanOrEqual(rows.length)
    expect(requeueButton('failed-1')).not.toBeNull()
    expect(requeueButton('done-1')).toBeNull()
    expect(requeueButton('idle-1')).toBeNull()
    expect(requeueButton('running-1')).toBeNull()
    expect(document.querySelectorAll('.requeue-btn')).toHaveLength(1)
  })

  it('requeues the clicked row, disables the control while pending, then reloads the list', async () => {
    const button = requeueButton('failed-1')!
    button.click()
    await flush()

    expect(requeueService.requestAsync).toHaveBeenCalledExactlyOnceWith('failed-1')
    expect(requeueButton('failed-1')!.disabled).toBe(true)
    expect(listService.requestAsync).not.toHaveBeenCalled()

    requeueButton('failed-1')!.click()
    await flush()
    expect(requeueService.requestAsync).toHaveBeenCalledOnce()

    requeueGate.resolve()
    await flush()

    expect(listService.requestAsync).toHaveBeenCalledExactlyOnceWith(1, 20)
    expect(requeueButton('failed-1')!.disabled).toBe(false)
  })

  it('still reloads the list when the server refuses the requeue', async () => {
    const unhandled = vi.fn()
    process.on('unhandledRejection', unhandled)
    try {
      requeueButton('failed-1')!.click()
      await flush()

      requeueGate.reject(new Error('NotFailed'))
      await flush()

      expect(listService.requestAsync).toHaveBeenCalledOnce()
      expect(unhandled).not.toHaveBeenCalled()
    } finally {
      process.off('unhandledRejection', unhandled)
    }
  })
})
