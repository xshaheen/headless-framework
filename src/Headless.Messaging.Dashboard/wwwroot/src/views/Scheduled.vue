<template>
  <div class="scheduled-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Scheduled Deliveries</h2>
        <v-btn
          size="small"
          variant="outlined"
          color="primary"
          prepend-icon="mdi-refresh"
          :loading="isLoading"
          @click="loadRows()"
        >
          Refresh
        </v-btn>
      </div>

      <p class="page-description text-medium-emphasis">
        Pending published rows not yet claimed by a delivery attempt. Revoke deletes the row and
        leaves only a ledger trace; dispatch now moves the due instant to the provider clock and is
        picked up within about a minute by a delayed processor.
      </p>

      <!-- Filters -->
      <div class="filters-row mb-4">
        <v-select
          v-model="laneFilter"
          :items="laneOptions"
          label="Filter by lane"
          prepend-inner-icon="mdi-directions-fork"
          clearable
          class="lane-filter"
          @update:model-value="applyLaneFilter"
        />
        <v-text-field
          v-model="nameFilter"
          label="Filter by name"
          prepend-inner-icon="mdi-magnify"
          clearable
          class="filter-field"
          @update:model-value="debouncedLoad"
        />
      </div>

      <!-- Table -->
      <TableSkeleton v-if="isLoading" :rows="5" :columns="7" />

      <v-card v-else class="messages-card">
        <v-table density="comfortable" class="messages-table">
          <thead>
            <tr>
              <th>Storage ID</th>
              <th>Message ID</th>
              <th>Name</th>
              <th>Lane</th>
              <th>Due At</th>
              <th>Lease</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="rows.length === 0">
              <td colspan="7" class="text-center pa-6 text-medium-emphasis">
                No pending scheduled deliveries
              </td>
            </tr>
            <tr v-for="row in rows" :key="row.storageId">
              <td class="text-caption">{{ row.storageId }}</td>
              <td class="text-caption">{{ row.messageId }}</td>
              <td>{{ row.messageName }}</td>
              <td>
                <v-chip size="x-small" color="info" variant="tonal">{{ row.lane }}</v-chip>
              </td>
              <td class="text-caption">
                <v-tooltip :text="timeAgo(row.expectedDueAt)" location="top">
                  <template #activator="{ props: tp }">
                    <span v-bind="tp">{{ formatDateTime(row.expectedDueAt) }}</span>
                  </template>
                </v-tooltip>
              </td>
              <td>
                <v-chip
                  size="x-small"
                  :color="row.isLeased ? 'warning' : 'success'"
                  variant="tonal"
                >
                  {{ row.isLeased ? 'Leased' : 'Free' }}
                </v-chip>
              </td>
              <td class="text-no-wrap">
                <v-btn
                  icon="mdi-cancel"
                  size="x-small"
                  variant="text"
                  color="error"
                  title="Revoke"
                  @click="confirmAction('revoke', row)"
                />
                <v-btn
                  icon="mdi-clock-fast"
                  size="x-small"
                  variant="text"
                  color="primary"
                  title="Dispatch now"
                  :disabled="!canDispatchNow(row)"
                  @click="confirmAction('dispatch-now', row)"
                />
              </td>
            </tr>
          </tbody>
        </v-table>

        <PaginationFooter
          :page="pagination.currentPage.value"
          :page-size="pagination.pageSize.value"
          :total-count="pagination.totalCount.value"
          @update:page="pagination.handlePageChange"
          @update:page-size="pagination.handlePageSizeChange"
        />
      </v-card>

      <!-- Confirm Dialog -->
      <Teleport to="body">
        <component
          v-if="confirmDialog && confirmDialog.isOpen"
          :is="confirmDialog.Component"
          :is-open="confirmDialog.isOpen"
          :dialog-props="confirmDialog.propData"
          @close="confirmDialog.close()"
          @confirm="onConfirmAction"
        />
      </Teleport>
    </div>
  </div>
</template>

<script setup lang="ts">
defineOptions({ name: 'ScheduledView' })
import { ref, onUnmounted } from 'vue'
import { httpService, HttpError } from '@/services/http'
import {
  canDispatchNow,
  createScheduledDeliveryOperationRequest,
  describeOutcome,
  shouldReloadAfterOutcome,
  type ScheduledDeliveryAction,
  type ScheduledDeliveryOperationResult,
  type ScheduledDeliveryPage,
  type ScheduledDeliveryView,
  type OperatorActorRequiredProblem,
} from '@/services/scheduled'
import { useAlertStore } from '@/stores/alertStore'
import { usePagination } from '@/composables/usePagination'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { formatDateTime, timeAgo } from '@/utilities/dateTimeParser'
import TableSkeleton from '@/components/common/TableSkeleton.vue'
import PaginationFooter from '@/components/common/PaginationFooter.vue'
import type { MessageLane } from '@/components/MessageDetailDialog.vue'

const alertStore = useAlertStore()

const laneOptions: readonly MessageLane[] = ['Bus', 'Queue']
const laneFilter = ref<MessageLane | null>(null)
const nameFilter = ref('')
const isLoading = ref(false)
const rows = ref<ScheduledDeliveryView[]>([])
let debounceTimer: ReturnType<typeof setTimeout> | null = null
let loadGeneration = 0
let isExecuting = false
let pendingAction: (() => Promise<void>) | null = null

const confirmDialog = useDialog<ConfirmDialogProps>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

const pagination = usePagination(
  async (page: number, pageSize: number) => {
    await loadRows(page, pageSize)
    return { totalCount: pagination.totalCount.value }
  },
  { initialPage: 1, initialPageSize: 20 },
)

async function loadRows(page?: number, pageSize?: number) {
  const generation = ++loadGeneration
  isLoading.value = true
  try {
    const p = page ?? pagination.currentPage.value
    const ps = pageSize ?? pagination.pageSize.value
    const params = new URLSearchParams({
      currentPage: String(p),
      perPage: String(ps),
    })
    if (laneFilter.value) params.set('lane', laneFilter.value)
    if (nameFilter.value) params.set('name', nameFilter.value)

    const data = await httpService.get<ScheduledDeliveryPage>(`/scheduled?${params}`)
    if (generation !== loadGeneration) return
    rows.value = data.items || []
    pagination.totalCount.value = data.totalItems || 0
  } catch (error) {
    if (generation !== loadGeneration) return
    console.error('Failed to load scheduled deliveries:', error)
    alertStore.showError('Failed to load scheduled deliveries')
  } finally {
    if (generation === loadGeneration) isLoading.value = false
  }
}

function debouncedLoad() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    pagination.currentPage.value = 1
    loadRows()
  }, 400)
}

function applyLaneFilter() {
  pagination.currentPage.value = 1
  loadRows()
}

onUnmounted(() => {
  if (debounceTimer) clearTimeout(debounceTimer)
})

const actionLabels: Record<
  ScheduledDeliveryAction,
  { title: string; confirmText: string; color: string; icon: string }
> = {
  revoke: {
    title: 'Revoke Scheduled Delivery',
    confirmText: 'Revoke',
    color: '#f44336',
    icon: 'mdi-cancel',
  },
  'dispatch-now': {
    title: 'Dispatch Now',
    confirmText: 'Dispatch Now',
    color: '#1976d2',
    icon: 'mdi-clock-fast',
  },
}

function confirmAction(action: ScheduledDeliveryAction, row: ScheduledDeliveryView) {
  const label = actionLabels[action]
  pendingAction = async () => {
    const request = createScheduledDeliveryOperationRequest(row, action, crypto.randomUUID())
    try {
      const path = action === 'revoke' ? '/scheduled/revoke' : '/scheduled/dispatch-now'
      const result = await httpService.post<ScheduledDeliveryOperationResult>(path, request)
      const message = describeOutcome(action, result.outcome, result.isReplay)
      if (result.outcome === 'Applied' || result.isReplay) {
        alertStore.showSuccess(message)
      } else {
        alertStore.showError(message)
      }
      if (shouldReloadAfterOutcome(result.outcome)) {
        await loadRows()
      }
    } catch (error) {
      if (error instanceof HttpError && error.status === 403) {
        const problem = error.body as OperatorActorRequiredProblem | null
        alertStore.showError(problem?.remedy ?? 'Operator actions require an authenticated actor.')
        return
      }
      console.error(`Failed to ${action} scheduled delivery:`, error)
      alertStore.showError(`Failed to ${label.confirmText.toLowerCase()} the scheduled delivery`)
    }
  }
  const props = new ConfirmDialogProps()
  props.title = label.title
  props.text = `${label.title} for ${row.messageName} (${row.storageId})?`
  props.confirmText = label.confirmText
  props.confirmColor = label.color
  props.icon = label.icon
  props.iconColor = label.color
  confirmDialog.open(props)
}

async function onConfirmAction() {
  if (isExecuting) return
  isExecuting = true
  confirmDialog.close()
  try {
    if (pendingAction) {
      await pendingAction()
      pendingAction = null
    }
  } finally {
    isExecuting = false
  }
}

// Initial load
loadRows()
</script>

<style scoped>
.scheduled-page {
  padding: 20px 12px;
}

.page-content {
  max-width: 1240px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #e0e0e0;
}

.page-description {
  margin-bottom: 16px;
  max-width: 800px;
}

.filters-row {
  display: flex;
  gap: 12px;
}

.filter-field {
  flex: 1;
}

.lane-filter {
  flex: 0 0 180px;
}

.messages-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.messages-table {
  background: transparent !important;
}

@media (max-width: 768px) {
  .scheduled-page {
    padding: 12px;
  }

  .filters-row {
    flex-direction: column;
  }

  .lane-filter {
    flex-basis: auto;
  }
}
</style>
