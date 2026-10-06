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

const rows = [row('done-1', 'Done')]

let deleteGate = deferred()
let onRemoved: (() => void) | undefined

const listService = fakeService({ items: rows, totalCount: rows.length, pageNumber: 1, pageSize: 20 })
const deleteService = fakeService<object>({}, () => deleteGate.promise)

vi.mock('@/http/services/timeJobService', () => ({
  timeJobService: {
    getTimeJobsPaginated: () => listService,
    deleteTimeJob: () => deleteService,
    deleteTimeJobsBatch: () => fakeService({}),
    getTimeJobsGraphDataRange: () => fakeService([]),
    getTimeJobsGraphData: () => fakeService([]),
  },
}))

vi.mock('@/http/services/jobsService', () => ({
  jobsService: {
    requestCancel: () => fakeService({}),
    requeue: () => fakeService({}),
  },
}))

vi.mock('@/stores/connectionStore', () => ({
  useConnectionStore: () => ({ isInitialized: true }),
}))

// The real module opens a SignalR connection on import. The removal handler is captured so a test can deliver the
// notification the server sends after a committed delete.
vi.mock('@/hub/jobNotificationHub', () => {
  const noop = () => {}
  return {
    methodName: new Proxy({}, { get: (_, key) => String(key) }),
    default: {
      onReceiveAddTimeJob: noop,
      onReceiveUpdateTimeJob: noop,
      onReceiveDeleteTimeJob: (handler: () => void) => {
        onRemoved = handler
      },
      onReceiveAddTimeJobsBatch: noop,
      onReceiveJobProgress: noop,
      stopReceiver: noop,
    },
  }
})

vi.mock('vue-echarts', () => ({ default: { name: 'VChart', render: () => null }, THEME_KEY: 'ecTheme' }))

// The confirm dialog is replaced by a single button that emits `confirm` while the dialog is open, so the test drives
// the same confirm path the user does without rendering the real dialog.
vi.mock('@/components/common/ConfirmDialog.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    __esModule: true,
    default: defineComponent({
      props: { isOpen: Boolean },
      emits: ['confirm', 'close'],
      setup(props, { emit }) {
        return () =>
          props.isOpen ? h('button', { 'data-testid': 'confirm-delete', onClick: () => emit('confirm') }) : null
      },
    }),
  }
})
vi.mock('@/components/ChainJobsModal.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/common/JobRequestDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/timeJobComponents/CRUDTimeJobDialogComponent.vue', () => ({
  __esModule: true,
  default: { render: () => null },
}))

const TimeJob = (await import('@/views/TimeJob.vue')).default

const deleteRow = async (id: string) => {
  ;[...document.querySelectorAll<HTMLTableRowElement>('tbody tr')]
    .find((tr) => tr.textContent?.includes(`Fn${id}`))!
    .querySelector<HTMLButtonElement>('.delete-btn')!
    .click()
  await flush()
  document.querySelector<HTMLButtonElement>('[data-testid="confirm-delete"]')!.click()
  await flush()
}

describe('TimeJob delete action', () => {
  let mounted: Mounted

  beforeEach(async () => {
    deleteGate = deferred()
    onRemoved = undefined
    listService.requestAsync.mockClear()
    deleteService.requestAsync.mockClear()
    mounted = mountWithApp(TimeJob)
    await flush()
    listService.requestAsync.mockClear()
  })

  afterEach(() => mounted.unmount())

  it('reloads the list after the delete succeeds, without waiting for the hub', async () => {
    await deleteRow('done-1')

    expect(deleteService.requestAsync).toHaveBeenCalledExactlyOnceWith('done-1')
    expect(listService.requestAsync).not.toHaveBeenCalled()

    deleteGate.resolve()
    await flush()

    expect(listService.requestAsync).toHaveBeenCalledExactlyOnceWith(1, 20)
  })

  it('reloads the list when the delete fails', async () => {
    const unhandled = vi.fn()
    process.on('unhandledRejection', unhandled)
    try {
      await deleteRow('done-1')
      deleteGate.reject(new Error('delete refused'))
      await flush()

      expect(listService.requestAsync).toHaveBeenCalledOnce()
    } finally {
      process.off('unhandledRejection', unhandled)
    }
  })

  it('reloads once for a burst of removal notifications', async () => {
    expect(onRemoved).toBeDefined()

    for (let i = 0; i < 5; i++) {
      onRemoved!()
    }
    await new Promise((resolve) => setTimeout(resolve, 300))
    await flush()

    expect(listService.requestAsync).toHaveBeenCalledOnce()
  })
})
