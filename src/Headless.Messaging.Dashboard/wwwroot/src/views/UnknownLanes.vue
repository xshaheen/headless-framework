<template>
  <div class="unknown-lanes-page">
    <div class="page-content">
      <div class="page-header">
        <div>
          <h2 class="page-title">Unknown Lanes</h2>
          <p class="page-description">
            Read-only persisted rows whose delivery lane is not recognized. Content is never loaded.
          </p>
        </div>
        <v-btn
          size="small"
          variant="outlined"
          color="primary"
          prepend-icon="mdi-refresh"
          :loading="isLoading"
          @click="loadMessages()"
        >
          Refresh
        </v-btn>
      </div>

      <div class="filters-row mb-4">
        <v-select
          v-model="messageType"
          :items="messageTypeOptions"
          item-title="label"
          item-value="value"
          label="Storage direction"
          prepend-inner-icon="mdi-swap-vertical"
          class="type-filter"
          @update:model-value="applyTypeFilter"
        />
      </div>

      <TableSkeleton v-if="isLoading" :rows="5" :columns="8" />

      <v-card v-else class="messages-card">
        <v-table density="comfortable" class="messages-table">
          <thead>
            <tr>
              <th>Storage ID</th>
              <th>Direction</th>
              <th>Raw Lane</th>
              <th>Name</th>
              <th>Status</th>
              <th>Added</th>
              <th>Next Retry</th>
              <th>Locked Until</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="messages.length === 0">
              <td colspan="8" class="text-center pa-6 text-medium-emphasis">
                No unknown-lane rows found
              </td>
            </tr>
            <tr v-for="message in messages" :key="message.storageId">
              <td class="text-caption">{{ message.storageId }}</td>
              <td>{{ message.messageType === 'Publish' ? 'Published' : 'Received' }}</td>
              <td>
                <v-chip size="x-small" color="warning" variant="tonal">
                  {{ message.rawLane }}
                </v-chip>
              </td>
              <td>{{ message.name }}</td>
              <td>{{ message.statusName }}</td>
              <td class="text-caption">{{ formatOptionalDate(message.added) }}</td>
              <td class="text-caption">{{ formatOptionalDate(message.nextRetryAt) }}</td>
              <td class="text-caption">{{ formatOptionalDate(message.lockedUntil) }}</td>
            </tr>
          </tbody>
        </v-table>

        <PaginationFooter
          :page="pagination.currentPage.value"
          :page-size="pagination.pageSize.value"
          :page-size-options="[20, 50, 100, 200]"
          :total-count="pagination.totalCount.value"
          @update:page="pagination.handlePageChange"
          @update:page-size="pagination.handlePageSizeChange"
        />
      </v-card>
    </div>
  </div>
</template>

<script setup lang="ts">
defineOptions({ name: 'UnknownLanesView' })
import { ref } from 'vue'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import { usePagination } from '@/composables/usePagination'
import { formatDateTime } from '@/utilities/dateTimeParser'
import TableSkeleton from '@/components/common/TableSkeleton.vue'
import PaginationFooter from '@/components/common/PaginationFooter.vue'

type MessageType = 'Publish' | 'Subscribe'

interface UnknownLaneMessage {
  storageId: string
  messageType: MessageType
  rawLane: number
  name: string
  statusName: string
  added: string
  nextRetryAt: string | null
  lockedUntil: string | null
}

const alertStore = useAlertStore()
const messageType = ref<MessageType>('Publish')
const messageTypeOptions = [
  { label: 'Published', value: 'Publish' },
  { label: 'Received', value: 'Subscribe' },
] as const
const messages = ref<UnknownLaneMessage[]>([])
const isLoading = ref(false)
let loadGeneration = 0

const pagination = usePagination(
  async (page: number, pageSize: number) => {
    await loadMessages(page, pageSize)
    return { totalCount: pagination.totalCount.value }
  },
  { initialPage: 1, initialPageSize: 50, pageSizeOptions: [20, 50, 100, 200] },
)

async function loadMessages(page?: number, pageSize?: number) {
  const generation = ++loadGeneration
  isLoading.value = true
  try {
    const params = new URLSearchParams({
      messageType: messageType.value,
      currentPage: String(page ?? pagination.currentPage.value),
      perPage: String(pageSize ?? pagination.pageSize.value),
    })
    const data = await httpService.get<{ items: UnknownLaneMessage[]; totals: number }>(
      `/unknown-lanes?${params}`,
    )
    if (generation !== loadGeneration) return
    messages.value = data.items ?? []
    pagination.totalCount.value = data.totals ?? 0
  } catch (error) {
    if (generation !== loadGeneration) return
    console.error('Failed to load unknown-lane rows:', error)
    alertStore.showError('Failed to load unknown-lane diagnostics')
  } finally {
    if (generation === loadGeneration) isLoading.value = false
  }
}

function applyTypeFilter() {
  pagination.currentPage.value = 1
  loadMessages()
}

function formatOptionalDate(value: string | null): string {
  return value ? formatDateTime(value) : '—'
}

loadMessages()
</script>

<style scoped>
.unknown-lanes-page {
  padding: 20px 12px;
}

.page-content {
  max-width: 1240px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
}

.page-title {
  margin: 0;
  color: #e0e0e0;
  font-size: 1.5rem;
  font-weight: 700;
}

.page-description {
  margin: 4px 0 0;
  color: rgba(255, 255, 255, 0.62);
}

.filters-row {
  display: flex;
}

.type-filter {
  max-width: 240px;
}

.messages-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.messages-table {
  background: transparent !important;
}

@media (max-width: 768px) {
  .unknown-lanes-page {
    padding: 12px;
  }

  .page-header {
    align-items: stretch;
    flex-direction: column;
  }

  .type-filter {
    max-width: none;
  }
}
</style>
