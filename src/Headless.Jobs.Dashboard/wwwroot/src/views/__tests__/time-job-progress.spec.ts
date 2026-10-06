import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'
import type { JobProgressNotification } from '@/utilities/job-progress'

const row = (id: string, status: string, progress: { percent: number; message: string } | null = null) => ({
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
  progressPercent: progress?.percent ?? null,
  progressMessage: progress?.message ?? null,
  progressUpdatedAt: progress ? '2026-01-01T00:00:00Z' : null,
})

const listService = fakeService({
  items: [row('running-1', 'InProgress'), row('failed-1', 'Failed', { percent: 42, message: 'Batch 3' })],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
})

const hubHandlers: { progress?: (notification: JobProgressNotification) => void } = {}

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
  jobsService: { requestCancel: () => fakeService({}), requeue: () => fakeService({}) },
}))

vi.mock('@/stores/connectionStore', () => ({
  useConnectionStore: () => ({ isInitialized: true }),
}))

// The real module opens a SignalR connection on import; this fake keeps the progress callback so a test can push one.
vi.mock('@/hub/jobNotificationHub', () => {
  const noop = () => {}
  return {
    methodName: new Proxy({}, { get: (_, key) => String(key) }),
    default: {
      onReceiveAddTimeJob: noop,
      onReceiveUpdateTimeJob: noop,
      onReceiveDeleteTimeJob: noop,
      onReceiveAddTimeJobsBatch: noop,
      onReceiveJobProgress: (callback: (notification: JobProgressNotification) => void) => {
        hubHandlers.progress = callback
      },
      stopReceiver: noop,
    },
  }
})

// Charts render to canvas, which happy-dom does not implement.
vi.mock('vue-echarts', () => ({ default: { name: 'VChart', render: () => null }, THEME_KEY: 'ecTheme' }))
vi.mock('@/components/ChainJobsModal.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/JobRequestDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/timeJobComponents/CRUDTimeJobDialogComponent.vue', () => ({
  __esModule: true,
  default: { render: () => null },
}))

// Imported after the mocks are registered and the fakes above exist, because the mock factories close over them.
const TimeJob = (await import('@/views/TimeJob.vue')).default

const progressIn = (id: string) =>
  [...document.querySelectorAll<HTMLTableRowElement>('tbody tr')]
    .find((tr) => tr.textContent?.includes(`Fn${id}`))
    ?.querySelector<HTMLElement>('[data-testid="job-progress"]') ?? null

describe('TimeJob progress', () => {
  let mounted: Mounted

  beforeEach(async () => {
    hubHandlers.progress = undefined
    mounted = mountWithApp(TimeJob)
    await flush()
    listService.requestAsync.mockClear()
  })

  afterEach(() => mounted.unmount())

  it('shows stored progress muted on a finished row and nothing on a running row that has not reported', () => {
    expect(progressIn('failed-1')?.classList).toContain('job-progress--muted')
    expect(progressIn('failed-1')?.textContent).toContain('42%')
    expect(progressIn('running-1')).toBeNull()
  })

  it('patches the running row from a progress notification without reloading the page', async () => {
    expect(hubHandlers.progress).toBeTypeOf('function')

    hubHandlers.progress!({
      id: 'running-1',
      type: 1,
      percent: 12.34,
      message: 'Copying files',
      updatedAt: '2026-01-01T00:00:05Z',
    })
    hubHandlers.progress!({ id: 'not-on-this-page', type: 1, percent: 50, message: null, updatedAt: '' })
    await flush()

    const cell = progressIn('running-1')
    expect(cell).not.toBeNull()
    expect(cell!.classList).not.toContain('job-progress--muted')
    expect(cell!.textContent).toContain('12.3%')
    expect(cell!.textContent).toContain('Copying files')
    expect(listService.requestAsync).not.toHaveBeenCalled()
  })
})
