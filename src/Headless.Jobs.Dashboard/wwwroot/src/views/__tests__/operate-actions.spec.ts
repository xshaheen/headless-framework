import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeService, flush, mountWithApp, type Mounted } from '@/__tests__/mount'

const cronRow = {
  id: 'cron-1',
  function: 'NightlyReport',
  expression: '0 0 * * *',
  initIdentifier: undefined,
  retryIntervals: [],
  description: '',
  requestType: '',
  createdAt: '',
  updatedAt: '',
  retries: 0,
}

const listService = fakeService({ items: [cronRow], totalCount: 1, pageNumber: 1, pageSize: 20 })

vi.mock('@/http/services/cronJobService', () => ({
  cronJobService: {
    getCronJobsPaginated: () => listService,
    getTimeJobsGraphDataRange: () => fakeService([]),
    getTimeJobsGraphDataRangeById: () => fakeService([]),
    getTimeJobsGraphData: () => fakeService([]),
    deleteCronJob: () => fakeService({}),
    runCronJobOnDemand: () => fakeService({}),
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
      onReceiveAddCronJob: noop,
      onReceiveUpdateCronJob: noop,
      onReceiveDeleteCronJob: noop,
      stopReceiver: noop,
    },
  }
})

vi.mock('vue-echarts', () => ({ default: { name: 'VChart', render: () => null }, THEME_KEY: 'ecTheme' }))
vi.mock('@/components/common/ConfirmDialog.vue', () => ({ __esModule: true, default: { render: () => null } }))
vi.mock('@/components/cronJobComponents/CronOccurrenceDialog.vue', () => ({
  __esModule: true,
  default: { render: () => null },
}))
vi.mock('@/components/cronJobComponents/CRUDCronJobDialog.vue', () => ({
  __esModule: true,
  default: { render: () => null },
}))

const CronJob = (await import('@/views/CronJob.vue')).default
const { useAuthStore } = await import('@/stores/authStore')

const rowAction = (name: string) => document.querySelector(`tbody [aria-label="${name}"]`)
const addJobButton = () =>
  Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes('Add Job'))

describe('write actions in host mode', () => {
  let mounted: Mounted | undefined

  beforeEach(() => {
    window.JobsConfig = { basePath: '/jobs/dashboard', auth: { mode: 'host', enabled: true, signInAvailable: true } }
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = undefined
    vi.unstubAllGlobals()
    delete window.JobsConfig
  })

  const mountSignedIn = async (canOperate: boolean) => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ mode: 'host', enabled: true, authenticated: true, name: 'ops', canOperate }),
      }),
    )
    mounted = mountWithApp(CronJob)
    await useAuthStore().initializeAuth()
    await flush()
  }

  it('hides every write action from a user who may only view, and keeps the read actions', async () => {
    await mountSignedIn(false)

    expect(rowAction('View occurrences')).not.toBeNull()
    expect(rowAction('Edit cron job')).toBeNull()
    expect(rowAction('Run now')).toBeNull()
    expect(rowAction('Delete cron job')).toBeNull()
    expect(addJobButton()).toBeUndefined()
  })

  it('shows the write actions to a user who may operate', async () => {
    await mountSignedIn(true)

    expect(rowAction('Edit cron job')).not.toBeNull()
    expect(rowAction('Run now')).not.toBeNull()
    expect(rowAction('Delete cron job')).not.toBeNull()
    expect(addJobButton()).toBeDefined()
  })
})
