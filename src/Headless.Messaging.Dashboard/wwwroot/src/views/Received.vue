<template>
  <div class="received-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Received Messages</h2>
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

      <!-- Status Tabs -->
      <v-tabs v-model="activeStatus" class="status-tabs mb-4">
        <v-tab v-for="status in statusTabs" :key="status.value" :value="status.value">
          <span class="tab-label-with-badge">
            {{ status.label }}
            <v-chip
              v-if="status.badgeCount !== undefined"
              :color="status.badgeColor"
              size="x-small"
              variant="tonal"
              class="ml-1"
            >{{ status.badgeCount }}</v-chip>
            <v-tooltip v-if="status.tooltip" location="bottom" max-width="300">
              <template #activator="{ props: tp }">
                <v-icon v-bind="tp" size="14" class="ml-1 status-info-icon">mdi-information-outline</v-icon>
              </template>
              {{ status.tooltip }}
            </v-tooltip>
          </span>
        </v-tab>
      </v-tabs>

      <!-- Filters -->
      <div class="filters-row mb-4">
        <v-text-field
          v-model="nameFilter"
          label="Filter by name"
          prepend-inner-icon="mdi-magnify"
          clearable
          class="filter-field"
          @update:model-value="debouncedLoad"
        />
        <v-text-field
          v-model="groupFilter"
          label="Filter by group"
          prepend-inner-icon="mdi-group"
          clearable
          class="filter-field"
          @update:model-value="debouncedLoad"
        />
        <v-text-field
          v-model="contentFilter"
          label="Filter by content"
          prepend-inner-icon="mdi-text-search"
          clearable
          class="filter-field"
          @update:model-value="debouncedLoad"
        />
      </div>

      <!-- Batch Actions -->
      <div v-if="selectedIds.length > 0" class="batch-actions mb-3">
        <v-chip size="small" color="primary" variant="tonal" class="mr-2">
          {{ selectedIds.length }} selected
        </v-chip>
        <v-btn
          size="small"
          color="warning"
          variant="elevated"
          prepend-icon="mdi-replay"
          class="mr-2"
          @click="handleBatchReexecute"
        >
          Re-execute Selected
        </v-btn>
        <v-btn
          size="small"
          color="error"
          variant="elevated"
          prepend-icon="mdi-delete"
          @click="handleBatchDelete"
        >
          Delete Selected
        </v-btn>
      </div>

      <!-- Table -->
      <TableSkeleton v-if="isLoading" :rows="5" :columns="8" />

      <v-card v-else class="messages-card">
        <v-table density="comfortable" class="messages-table">
          <thead>
            <tr>
              <th style="width: 40px">
                <v-checkbox
                  v-model="selectAll"
                  hide-details
                  density="compact"
                  @update:model-value="toggleSelectAll"
                />
              </th>
              <th>Storage ID</th>
              <th>Message ID</th>
              <th>Name</th>
              <th>Group</th>
              <th>Added</th>
              <th>Expires At</th>
              <th>Retries</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="messages.length === 0">
              <td colspan="8" class="text-center pa-6 text-medium-emphasis">No messages found</td>
            </tr>
            <tr v-for="msg in messages" :key="msg.storageId">
              <td>
                <v-checkbox
                  :model-value="selectedIds.includes(msg.storageId)"
                  hide-details
                  density="compact"
                  @update:model-value="toggleSelect(msg.storageId)"
                />
              </td>
              <td class="text-caption">
                <a class="id-link" @click="viewMessage(msg.storageId)">{{ msg.storageId }}</a>
              </td>
              <td class="text-caption">{{ msg.messageId }}</td>
              <td>{{ msg.name }}</td>
              <td>{{ msg.group }}</td>
              <td class="text-caption">
                <v-tooltip :text="timeAgo(msg.added)" location="top">
                  <template #activator="{ props: tp }">
                    <span v-bind="tp">{{ formatDateTime(msg.added) }}</span>
                  </template>
                </v-tooltip>
              </td>
              <td class="text-caption">
                <v-tooltip :text="timeAgo(msg.expiresAt)" location="top">
                  <template #activator="{ props: tp }">
                    <span v-bind="tp">{{ formatDateTime(msg.expiresAt) }}</span>
                  </template>
                </v-tooltip>
              </td>
              <td>{{ msg.retries }}</td>
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

      <!-- Message Detail Dialog -->
      <MessageDetailDialog v-model="detailDialogOpen" :message="detailMessage" />

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
import { ref, computed, watch, onUnmounted } from 'vue'
import { storeToRefs } from 'pinia'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import { useMessagingStore } from '@/stores/messagingStore'
import { usePagination } from '@/composables/usePagination'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { formatDateTime, timeAgo } from '@/utilities/dateTimeParser'
import TableSkeleton from '@/components/common/TableSkeleton.vue'
import PaginationFooter from '@/components/common/PaginationFooter.vue'
import MessageDetailDialog, { type MessageDetail } from '@/components/MessageDetailDialog.vue'

interface ReceivedMessage {
  storageId: string
  messageId: string
  name: string
  group: string
  added: string
  expiresAt: string
  retries: number
  statusName: string
}

const alertStore = useAlertStore()
const messagingStore = useMessagingStore()
const { stats } = storeToRefs(messagingStore)

const statusTabs = computed(() => [
  {
    label: 'Succeeded',
    value: 'Succeeded',
    badgeCount: stats.value.receivedSucceeded,
    badgeColor: 'success',
    tooltip: 'Messages consumed successfully by their subscriber.',
  },
  {
    label: 'Failed',
    value: 'Failed',
    badgeCount: stats.value.receivedFailed,
    badgeColor: 'error',
    tooltip: 'Messages whose consumer threw an exception after all retry attempts. Can be re-executed manually.',
  },
  {
    label: 'Delayed',
    value: 'Delayed',
    tooltip: 'Messages with deferred consumption (delay > 1 min). Shorter delays show as "Queued".',
  },
  {
    label: 'Scheduled',
    value: 'Scheduled',
    tooltip: 'Messages picked up by the processor and awaiting consumer execution.',
  },
  {
    label: 'Queued',
    value: 'Queued',
    tooltip: 'Messages waiting to be dispatched to their consumer.',
  },
])

const activeStatus = ref('Succeeded')
const nameFilter = ref('')
const groupFilter = ref('')
const contentFilter = ref('')
const isLoading = ref(false)
const messages = ref<ReceivedMessage[]>([])
const selectedIds = ref<string[]>([])
const selectAll = ref(false)
const detailDialogOpen = ref(false)
const detailMessage = ref<MessageDetail | null>(null)
let pendingAction: (() => Promise<void>) | null = null
let debounceTimer: ReturnType<typeof setTimeout> | null = null
let isExecuting = false
let loadGeneration = 0

const confirmDialog = useDialog<ConfirmDialogProps>().withComponent(
  () => import('@/components/common/ConfirmDialog.vue'),
)

const pagination = usePagination(
  async (page: number, pageSize: number) => {
    await loadMessages(page, pageSize)
    return { totalCount: pagination.totalCount.value }
  },
  { initialPage: 1, initialPageSize: 20 },
)

async function loadMessages(page?: number, pageSize?: number) {
  const generation = ++loadGeneration
  isLoading.value = true
  try {
    const p = page ?? pagination.currentPage.value
    const ps = pageSize ?? pagination.pageSize.value
    const params = new URLSearchParams({
      currentPage: String(p),
      perPage: String(ps),
    })
    if (nameFilter.value) params.set('name', nameFilter.value)
    if (groupFilter.value) params.set('group', groupFilter.value)
    if (contentFilter.value) params.set('content', contentFilter.value)

    const [data] = await Promise.all([
      httpService.get<{ items: ReceivedMessage[]; totals: number }>(
        `/received/${activeStatus.value}?${params}`,
      ),
      messagingStore.fetchStats(),
    ])
    if (generation !== loadGeneration) return
    messages.value = data.items || []
    pagination.totalCount.value = data.totals || 0
    selectedIds.value = []
    selectAll.value = false
  } catch (error) {
    if (generation !== loadGeneration) return
    console.error('Failed to load received messages:', error)
    alertStore.showError('Failed to load received messages')
  } finally {
    if (generation === loadGeneration) isLoading.value = false
  }
}

function debouncedLoad() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    pagination.currentPage.value = 1
    loadMessages()
  }, 400)
}

watch(activeStatus, () => {
  pagination.currentPage.value = 1
  loadMessages()
})

onUnmounted(() => {
  if (debounceTimer) clearTimeout(debounceTimer)
})

function toggleSelectAll(checked: boolean | null) {
  if (checked) {
    selectedIds.value = messages.value.map((m) => m.storageId)
  } else {
    selectedIds.value = []
  }
}

function toggleSelect(storageId: string) {
  const idx = selectedIds.value.indexOf(storageId)
  if (idx >= 0) {
    selectedIds.value.splice(idx, 1)
  } else {
    selectedIds.value.push(storageId)
  }
  selectAll.value = selectedIds.value.length === messages.value.length && messages.value.length > 0
}

async function viewMessage(storageId: string) {
  try {
    const dto = await httpService.get<MessageDetail>(`/received/message/${storageId}`)
    detailMessage.value = dto
    detailDialogOpen.value = true
  } catch (error) {
    alertStore.showError('Failed to load message detail')
  }
}

function handleBatchReexecute() {
  pendingAction = async () => {
    try {
      await httpService.post('/received/reexecute', [...selectedIds.value])
      alertStore.showSuccess(`${selectedIds.value.length} messages re-executed`)
      await loadMessages()
    } catch (error) {
      alertStore.showError('Failed to re-execute messages')
    }
  }
  const props = new ConfirmDialogProps()
  props.title = 'Re-execute Messages'
  props.text = `Are you sure you want to re-execute ${selectedIds.value.length} messages?`
  props.confirmText = 'Re-execute All'
  props.confirmColor = '#ff9800'
  props.icon = 'mdi-replay'
  props.iconColor = '#ff9800'
  confirmDialog.open(props)
}

function handleBatchDelete() {
  pendingAction = async () => {
    try {
      await httpService.post('/received/delete', [...selectedIds.value])
      alertStore.showSuccess(`${selectedIds.value.length} messages deleted`)
      await loadMessages()
    } catch (error) {
      alertStore.showError('Failed to delete messages')
    }
  }
  const props = new ConfirmDialogProps()
  props.title = 'Delete Messages'
  props.text = `Are you sure you want to delete ${selectedIds.value.length} messages? This action cannot be undone.`
  props.confirmText = 'Delete All'
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
loadMessages()
</script>

<style scoped>
.received-page {
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
  margin-bottom: 16px;
}

.tab-label-with-badge {
  display: flex;
  align-items: center;
}

.status-info-icon {
  opacity: 0.45;
  cursor: help;
  transition: opacity 0.15s;
}

.status-info-icon:hover {
  opacity: 0.85;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #e0e0e0;
}

.status-tabs {
  background: rgba(30, 30, 30, 0.6);
  border-radius: 8px;
}

.filters-row {
  display: flex;
  gap: 12px;
}

.filter-field {
  flex: 1;
}

.batch-actions {
  display: flex;
  align-items: center;
}

.messages-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.messages-table {
  background: transparent !important;
}

.id-link {
  color: #90caf9;
  cursor: pointer;
  text-decoration: none;
}

.id-link:hover {
  text-decoration: underline;
}

@media (max-width: 768px) {
  .received-page {
    padding: 12px;
  }

  .filters-row {
    flex-direction: column;
  }
}
</style>
