<template>
  <div class="published-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Published Messages</h2>
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
            <v-tooltip
              v-if="status.value === 'Delayed'"
              activator="parent"
              location="bottom"
              max-width="300"
            >
              Only shows messages with delay time &gt; 1 minute. Messages with shorter delays have
              status "Queued" — check them in the database.
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
          prepend-icon="mdi-refresh"
          class="mr-2"
          @click="handleBatchRequeue"
        >
          {{ isDelayedTab ? 'Immediately Publish Selected' : 'Requeue Selected' }}
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
      <TableSkeleton v-if="isLoading" :rows="5" :columns="7" />

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
              <th>ID</th>
              <th>Name</th>
              <th>Content</th>
              <th>Added</th>
              <th>{{ isDelayedTab ? 'Delayed Publish Time' : 'Expires At' }}</th>
              <th>Retries</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="messages.length === 0">
              <td colspan="8" class="text-center pa-6 text-medium-emphasis">No messages found</td>
            </tr>
            <tr v-for="msg in messages" :key="msg.id">
              <td>
                <v-checkbox
                  :model-value="selectedIds.includes(msg.id)"
                  hide-details
                  density="compact"
                  @update:model-value="toggleSelect(msg.id)"
                />
              </td>
              <td class="text-caption">{{ msg.id }}</td>
              <td>{{ msg.name }}</td>
              <td class="content-cell">
                <span class="content-truncated">{{ truncate(msg.content, 60) }}</span>
              </td>
              <td class="text-caption">{{ formatDateTime(msg.added) }}</td>
              <td class="text-caption">{{ formatDateTime(msg.expiresAt) }}</td>
              <td>{{ msg.retries }}</td>
              <td>
                <v-btn
                  size="x-small"
                  variant="text"
                  icon="mdi-eye"
                  color="primary"
                  @click="viewMessage(msg.id)"
                />
                <v-tooltip :text="isDelayedTab ? 'Immediately Publish' : 'Requeue'" location="top">
                  <template #activator="{ props: tooltipProps }">
                    <v-btn
                      v-bind="tooltipProps"
                      size="x-small"
                      variant="text"
                      icon="mdi-refresh"
                      color="warning"
                      @click="requeueMessage(msg.id)"
                    />
                  </template>
                </v-tooltip>
                <v-btn
                  size="x-small"
                  variant="text"
                  icon="mdi-delete"
                  color="error"
                  @click="deleteMessage(msg.id)"
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
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import { useMessagingStore } from '@/stores/messagingStore'
import { usePagination } from '@/composables/usePagination'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { formatDateTime } from '@/utilities/dateTimeParser'
import TableSkeleton from '@/components/common/TableSkeleton.vue'
import PaginationFooter from '@/components/common/PaginationFooter.vue'
import MessageDetailDialog, { type MessageDetail } from '@/components/MessageDetailDialog.vue'

interface Message {
  id: number
  name: string
  content: string
  added: string
  expiresAt: string
  retries: number
  statusName: string
}

const alertStore = useAlertStore()
const messagingStore = useMessagingStore()
const { stats } = storeToRefs(messagingStore)

const activeStatus = ref('Succeeded')

const statusTabs = computed(() => [
  { label: 'Succeeded', value: 'Succeeded', badgeCount: stats.value.publishedSucceeded, badgeColor: 'success' },
  { label: 'Failed', value: 'Failed', badgeCount: stats.value.publishedFailed, badgeColor: 'error' },
  { label: 'Delayed', value: 'Delayed', badgeCount: stats.value.publishedDelayed, badgeColor: 'warning' },
  { label: 'Scheduled', value: 'Scheduled' },
  { label: 'Queued', value: 'Queued' },
])

const isDelayedTab = computed(() => activeStatus.value === 'Delayed')
const nameFilter = ref('')
const contentFilter = ref('')
const isLoading = ref(false)
const messages = ref<Message[]>([])
const selectedIds = ref<number[]>([])
const selectAll = ref(false)
const detailDialogOpen = ref(false)
const detailMessage = ref<MessageDetail | null>(null)
let pendingAction: (() => Promise<void>) | null = null
let debounceTimer: ReturnType<typeof setTimeout> | null = null

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
  isLoading.value = true
  try {
    const p = page || pagination.currentPage.value
    const ps = pageSize || pagination.pageSize.value
    const params = new URLSearchParams({
      currentPage: String(p),
      perPage: String(ps),
    })
    if (nameFilter.value) params.set('name', nameFilter.value)
    if (contentFilter.value) params.set('content', contentFilter.value)

    const data = await httpService.get<{ items: Message[]; totals: number }>(
      `/published/${activeStatus.value}?${params}`,
    )
    messages.value = data.items || []
    pagination.totalCount.value = data.totals || 0
    selectedIds.value = []
    selectAll.value = false
  } catch (error) {
    console.error('Failed to load published messages:', error)
    alertStore.showError('Failed to load published messages')
  } finally {
    isLoading.value = false
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

function truncate(text: string, max: number): string {
  if (!text) return ''
  return text.length > max ? text.substring(0, max) + '...' : text
}

function toggleSelectAll(checked: boolean | null) {
  if (checked) {
    selectedIds.value = messages.value.map((m) => m.id)
  } else {
    selectedIds.value = []
  }
}

function toggleSelect(id: number) {
  const idx = selectedIds.value.indexOf(id)
  if (idx >= 0) {
    selectedIds.value.splice(idx, 1)
  } else {
    selectedIds.value.push(id)
  }
  selectAll.value = selectedIds.value.length === messages.value.length && messages.value.length > 0
}

async function viewMessage(id: number) {
  try {
    const dto = await httpService.get<MessageDetail>(`/published/message/${id}`)
    detailMessage.value = dto
    detailDialogOpen.value = true
  } catch (error) {
    alertStore.showError('Failed to load message detail')
  }
}

function requeueMessage(id: number) {
  pendingAction = async () => {
    try {
      await httpService.post('/published/requeue', [id])
      alertStore.showSuccess('Message requeued')
      await loadMessages()
    } catch (error) {
      alertStore.showError('Failed to requeue message')
    }
  }
  const props = new ConfirmDialogProps()
  props.title = 'Requeue Message'
  props.text = 'Are you sure you want to requeue this message?'
  props.confirmText = 'Requeue'
  props.confirmColor = '#ff9800'
  props.icon = 'mdi-refresh'
  props.iconColor = '#ff9800'
  confirmDialog.open(props)
}

function deleteMessage(id: number) {
  pendingAction = async () => {
    try {
      await httpService.post('/published/delete', [id])
      alertStore.showSuccess('Message deleted')
      await loadMessages()
    } catch (error) {
      alertStore.showError('Failed to delete message')
    }
  }
  const props = new ConfirmDialogProps()
  props.title = 'Delete Message'
  props.text = 'Are you sure you want to delete this message? This action cannot be undone.'
  props.confirmText = 'Delete'
  confirmDialog.open(props)
}

function handleBatchRequeue() {
  pendingAction = async () => {
    try {
      await httpService.post('/published/requeue', [...selectedIds.value])
      alertStore.showSuccess(`${selectedIds.value.length} messages requeued`)
      await loadMessages()
    } catch (error) {
      alertStore.showError('Failed to requeue messages')
    }
  }
  const props = new ConfirmDialogProps()
  props.title = 'Requeue Messages'
  props.text = `Are you sure you want to requeue ${selectedIds.value.length} messages?`
  props.confirmText = 'Requeue All'
  props.confirmColor = '#ff9800'
  props.icon = 'mdi-refresh'
  props.iconColor = '#ff9800'
  confirmDialog.open(props)
}

function handleBatchDelete() {
  pendingAction = async () => {
    try {
      await httpService.post('/published/delete', [...selectedIds.value])
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
  confirmDialog.close()
  if (pendingAction) {
    await pendingAction()
    pendingAction = null
  }
}

// Initial load
loadMessages()
</script>

<style scoped>
.published-page {
  padding: 20px;
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

.content-cell {
  max-width: 250px;
}

.content-truncated {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #bdbdbd;
  font-size: 0.8rem;
}

@media (max-width: 768px) {
  .published-page {
    padding: 12px;
  }

  .filters-row {
    flex-direction: column;
  }
}
</style>
