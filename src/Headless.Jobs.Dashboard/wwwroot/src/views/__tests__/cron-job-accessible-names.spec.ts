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

const label = (selector: string) => document.querySelector(selector)?.getAttribute('aria-label')

describe('CronJob accessible names', () => {
  let mounted: Mounted

  beforeEach(async () => {
    mounted = mountWithApp(CronJob)
    await flush()
  })

  afterEach(() => mounted.unmount())

  it('names every icon-only row action', () => {
    expect(label('tbody .occurrences-btn')).toBe('View occurrences')
    expect(label('tbody .edit-btn')).toBe('Edit cron job')
    expect(label('tbody .run-btn')).toBe('Run now')
    expect(label('tbody .delete-btn')).toBe('Delete cron job')
  })

  it('names the chart button for the chart it shows, apart from the occurrences button', async () => {
    const chartButton = () => document.querySelector<HTMLButtonElement>('tbody .chart-btn')!

    expect(chartButton().getAttribute('aria-label')).toBe('Show occurrence chart')
    expect(chartButton().getAttribute('aria-pressed')).toBe('false')

    chartButton().click()
    await flush()

    expect(chartButton().getAttribute('aria-label')).toBe('Show chart for all cron jobs')
    expect(chartButton().getAttribute('aria-pressed')).toBe('true')
  })

  it('names the toolbar and chart controls by their action', async () => {
    expect(label('.refresh-btn')).toBe('Refresh')
    expect(label('.reset-btn')).toBe('Reset chart range')

    const [timeChart, statusChart] = document.querySelectorAll<HTMLButtonElement>('.chart-btn-vertical')
    expect(timeChart!.getAttribute('aria-label')).toBe('Show time chart')
    expect(timeChart!.getAttribute('aria-pressed')).toBe('true')
    expect(statusChart!.getAttribute('aria-label')).toBe('Show status chart')
    expect(statusChart!.getAttribute('aria-pressed')).toBe('false')

    statusChart!.click()
    await flush()

    expect(label('.refresh-chart-btn')).toBe('Refresh status chart')
    expect(statusChart!.getAttribute('aria-pressed')).toBe('true')
  })
})
