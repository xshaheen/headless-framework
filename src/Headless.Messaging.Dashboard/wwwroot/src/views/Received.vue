<template>
  <div class="received-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Received Messages</h2>
      </div>

      <!-- Status Tabs -->
      <v-tabs v-model="activeStatus" class="status-tabs mb-4">
        <v-tab v-for="status in statusTabs" :key="status.value" :value="status.value">
          {{ status.label }}
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
              <th>ID</th>
              <th>Name</th>
              <th>Group</th>
              <th>Content</th>
              <th>Added</th>
              <th>Expires At</th>
              <th>Retries</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="messages.length === 0">
              <td colspan="9" class="text-center pa-6 text-medium-emphasis">No messages found</td>
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
              <td>{{ msg.group }}</td>
              <td class="content-cell">
                <span class="content-truncated">{{ truncate(msg.content, 50) }}</span>
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
                <v-btn
                  size="x-small"
                  variant="text"
                  icon="mdi-replay"
                  color="warning"
                  @click="reexecuteMessage(msg.id)"
                />
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
import { ref, watch } from 'vue'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import { usePagination } from '@/composables/usePagination'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { formatDateTime } from '@/utilities/dateTimeParser'
import TableSkeleton from '@/components/common/TableSkeleton.vue'
import PaginationFooter from '@/components/common/PaginationFooter.vue'
import MessageDetailDialog, { type MessageDetail } from '@/components/MessageDetailDialog.vue'

interface ReceivedMessage {
  id: number
  name: string
  group: string
  content: string
  added: string
  expiresAt: string
  retries: number
  statusName: string
}

const alertStore = useAlertStore()

const statusTabs = [
  { label: 'Succeeded', value: 'Succeeded' },
  { label: 'Failed', value: 'Failed' },
  { label: 'Delayed', value: 'Delayed' },
  { label: 'Scheduled', value: 'Scheduled' },
  { label: 'Queued', value: 'Queued' },
]

const activeStatus = ref('Succeeded')
const nameFilter = ref('')
const groupFilter = ref('')
const contentFilter = ref('')
const isLoading = ref(false)
const messages = ref<ReceivedMessage[]>([])
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
    if (groupFilter.value) params.set('group', groupFilter.value)
    if (contentFilter.value) params.set('content', contentFilter.value)

    const data = await httpService.get<{ items: ReceivedMessage[]; totals: number }>(
      `/received/${activeStatus.value}?${params}`,
    )
    messages.value = data.items || []
    pagination.totalCount.value = data.totals || 0
    selectedIds.value = []
    selectAll.value = false
  } catch (error) {
    console.error('Failed to load received messages:', error)
    alertStore.showError('Failed to load received messages')
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
    const dto = await httpService.get<MessageDetail>(`/received/message/${id}`)
    detailMessage.value = dto
    detailDialogOpen.value = true
  } catch (error) {
    alertStore.showError('Failed to load message detail')
  }
}

function reexecuteMessage(id: number) {
  pendingAction = async () => {
    try {
      await httpService.post('/received/reexecute', [id])
      alertStore.showSuccess('Message re-executed')
      await loadMessages()
    } catch (error) {
      alertStore.showError('Failed to re-execute message')
    }
  }
  const props = new ConfirmDialogProps()
  props.title = 'Re-execute Message'
  props.text = 'Are you sure you want to re-execute this message?'
  props.confirmText = 'Re-execute'
  props.confirmColor = '#ff9800'
  props.icon = 'mdi-replay'
  props.iconColor = '#ff9800'
  confirmDialog.open(props)
}

function deleteMessage(id: number) {
  pendingAction = async () => {
    try {
      await httpService.post('/received/delete', [id])
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
.received-page {
  padding: 20px;
}

.page-content {
  max-width: 1240px;
  margin: 0 auto;
}

.page-header {
  margin-bottom: 16px;
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
  max-width: 200px;
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
  .received-page {
    padding: 12px;
  }

  .filters-row {
    flex-direction: column;
  }
}
</style>
