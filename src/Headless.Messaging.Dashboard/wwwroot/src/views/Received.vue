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
              >{{ status.badgeCount }}</v-chip
            >
            <v-tooltip v-if="status.tooltip" location="bottom" max-width="300">
              <template #activator="{ props: tp }">
                <v-icon v-bind="tp" size="14" class="ml-1 status-info-icon"
                  >mdi-information-outline</v-icon
                >
              </template>
              {{ status.tooltip }}
            </v-tooltip>
          </span>
        </v-tab>
      </v-tabs>

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

      <v-card v-if="canLoadInbox" class="messages-card mb-4">
        <v-card-title class="text-subtitle-1">Authorized Inbox Generations</v-card-title>
        <v-card-text>
          <v-table density="compact">
            <thead>
              <tr>
                <th>Tenant</th><th>Message</th><th>Consumer</th><th>Lane</th><th>Outcome</th>
                <th>Tier</th><th>Generation</th>
                <th>Provenance</th><th>Hold</th><th>Expires</th><th>Recovery</th><th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="inboxGenerations.length === 0">
                <td colspan="12" class="text-center pa-4 text-medium-emphasis">No retained inbox generations</td>
              </tr>
              <tr v-for="generation in inboxGenerations" :key="generation.incarnationId">
                <td>{{ generation.tenantId ?? '—' }}</td>
                <td>{{ generation.messageId }}</td>
                <td>{{ generation.consumerIdentity }}</td>
                <td>{{ generation.lane }}</td>
                <td>{{ generation.status }}</td>
                <td>{{ inboxTier }}</td>
                <td>{{ generation.generation }}</td>
                <td>{{ generation.replayParentIncarnationId ? 'Replay' : 'Original' }}</td>
                <td>{{ generation.isHeld ? 'Held' : 'Released' }}</td>
                <td>{{ generation.effectiveExpiresAt ? formatDateTime(generation.effectiveExpiresAt) : '—' }}</td>
                <td>{{ generation.isOrphaned ? 'Orphaned' : 'Routable' }}</td>
                <td class="text-no-wrap">
                  <v-btn
                    icon="mdi-replay"
                    size="x-small"
                    variant="text"
                    color="warning"
                    title="Force reprocess"
                    :disabled="!isTerminalInboxGeneration(generation.status)"
                    @click="confirmInboxOperation('reexecute', generation)"
                  />
                  <v-btn
                    :icon="generation.isHeld ? 'mdi-lock-open-variant' : 'mdi-lock'"
                    size="x-small"
                    variant="text"
                    color="info"
                    :title="generation.isHeld ? 'Release hold' : 'Hold generation'"
                    :disabled="!isTerminalInboxGeneration(generation.status)"
                    @click="confirmInboxOperation(generation.isHeld ? 'release' : 'hold', generation)"
                  />
                  <v-btn
                    icon="mdi-delete"
                    size="x-small"
                    variant="text"
                    color="error"
                    title="Purge generation"
                    :disabled="!isTerminalInboxGeneration(generation.status)"
                    @click="confirmInboxOperation('delete', generation)"
                  />
                </td>
              </tr>
            </tbody>
          </v-table>
        </v-card-text>
      </v-card>

      <!-- Table -->
      <TableSkeleton v-if="isLoading" :rows="5" :columns="10" />

      <v-card v-else class="messages-card">
        <v-table density="comfortable" class="messages-table">
          <thead>
            <tr>
              <th>Storage ID</th>
              <th>Message ID</th>
              <th>Name</th>
              <th>Group</th>
              <th>Lane</th>
              <th>Requested</th>
              <th>Resolved</th>
              <th>Added</th>
              <th>Expires At</th>
              <th>Retries</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="messages.length === 0">
              <td colspan="10" class="text-center pa-6 text-medium-emphasis">No messages found</td>
            </tr>
            <tr v-for="msg in messages" :key="msg.storageId">
              <td class="text-caption">
                <a class="id-link" @click="viewMessage(msg.storageId)">{{ msg.storageId }}</a>
              </td>
              <td class="text-caption">{{ msg.messageId }}</td>
              <td>{{ msg.name }}</td>
              <td>{{ msg.group }}</td>
              <td>
                <v-chip size="x-small" color="info" variant="tonal">{{ msg.lane }}</v-chip>
              </td>
              <td class="text-caption">{{ msg.requestedDeliveryMode ?? '—' }}</td>
              <td class="text-caption">{{ msg.resolvedDeliveryMode ?? '—' }}</td>
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
defineOptions({ name: 'ReceivedView' })
import { ref, computed, watch, onUnmounted } from 'vue'
import { storeToRefs } from 'pinia'
import { httpService } from '@/services/http'
import {
  createInboxOperationRequest,
  isTerminalInboxGeneration,
  type InboxGeneration,
} from '@/services/inbox'
import { useAlertStore } from '@/stores/alertStore'
import { useMessagingStore } from '@/stores/messagingStore'
import { usePagination } from '@/composables/usePagination'
import { useDialog } from '@/composables/useDialog'
import { ConfirmDialogProps } from '@/components/common/ConfirmDialog.vue'
import { formatDateTime, timeAgo } from '@/utilities/dateTimeParser'
import TableSkeleton from '@/components/common/TableSkeleton.vue'
import PaginationFooter from '@/components/common/PaginationFooter.vue'
import MessageDetailDialog, {
  type DeliveryMode,
  type MessageDetail,
  type MessageLane,
} from '@/components/MessageDetailDialog.vue'

interface ReceivedMessage {
  storageId: string
  messageId: string
  name: string
  group: string
  added: string
  expiresAt: string
  retries: number
  statusName: string
  lane: MessageLane
  requestedDeliveryMode: DeliveryMode | null
  resolvedDeliveryMode: DeliveryMode | null
}

interface DashboardMeta {
  providerCapabilities: Array<{ role: string; inboxCapability: string | null }>
}

const inboxActions = {
  reexecute: {
    path: '/received/reexecute',
    title: 'Force Reprocess Generation',
    confirmText: 'Force Reprocess',
    success: 'queued for reprocessing',
    reason: 'Dashboard force reprocess',
    color: '#ff9800',
    icon: 'mdi-replay',
  },
  hold: {
    path: '/inbox/hold',
    title: 'Hold Generation',
    confirmText: 'Hold',
    success: 'held',
    reason: 'Dashboard hold',
    color: '#2196f3',
    icon: 'mdi-lock',
  },
  release: {
    path: '/inbox/release',
    title: 'Release Generation Hold',
    confirmText: 'Release Hold',
    success: 'released',
    reason: 'Dashboard release hold',
    color: '#2196f3',
    icon: 'mdi-lock-open-variant',
  },
  delete: {
    path: '/received/delete',
    title: 'Purge Generation',
    confirmText: 'Purge',
    success: 'purged',
    reason: 'Dashboard purge',
    color: '#f44336',
    icon: 'mdi-delete',
  },
} as const

type InboxAction = keyof typeof inboxActions

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
    tooltip:
      'Messages whose consumer threw an exception after all retry attempts. Can be re-executed manually.',
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
const laneOptions: readonly MessageLane[] = ['Bus', 'Queue']
const laneFilter = ref<MessageLane | null>(null)
const nameFilter = ref('')
const groupFilter = ref('')
const contentFilter = ref('')
const isLoading = ref(false)
const messages = ref<ReceivedMessage[]>([])
const inboxGenerations = ref<InboxGeneration[]>([])
const inboxTier = ref('Unavailable')
const canLoadInbox = window.MessagingConfig?.auth?.enabled === true
const detailDialogOpen = ref(false)
const detailMessage = ref<MessageDetail | null>(null)
let pendingAction: (() => Promise<void>) | null = null
let debounceTimer: ReturnType<typeof setTimeout> | null = null
let isExecuting = false
let loadGeneration = 0
let metaRequest: Promise<DashboardMeta> | null = null

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

function loadProviderMeta(): Promise<DashboardMeta> {
  metaRequest ??= httpService.get<DashboardMeta>('/meta').catch((error) => {
    metaRequest = null
    throw error
  })
  return metaRequest
}

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
    if (laneFilter.value) params.set('lane', laneFilter.value)
    if (nameFilter.value) params.set('name', nameFilter.value)
    if (groupFilter.value) params.set('group', groupFilter.value)
    if (contentFilter.value) params.set('content', contentFilter.value)

    const inboxRequest = canLoadInbox
      ? httpService
          .get<{ items: InboxGeneration[] }>('/inbox?currentPage=1&perPage=20')
          .catch(() => ({ items: [] }))
      : Promise.resolve({ items: [] as InboxGeneration[] })
    const [data, inbox, meta] = await Promise.all([
      httpService.get<{ items: ReceivedMessage[]; totals: number }>(
        `/received/${activeStatus.value}?${params}`,
      ),
      inboxRequest,
      loadProviderMeta(),
      messagingStore.fetchStats(),
    ])
    if (generation !== loadGeneration) return
    messages.value = data.items || []
    inboxGenerations.value = inbox.items || []
    inboxTier.value =
      meta.providerCapabilities.find((capability) => capability.role === 'Storage')?.inboxCapability ??
      'Unavailable'
    pagination.totalCount.value = data.totals || 0
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

function applyLaneFilter() {
  pagination.currentPage.value = 1
  loadMessages()
}

watch(activeStatus, () => {
  pagination.currentPage.value = 1
  loadMessages()
})

onUnmounted(() => {
  if (debounceTimer) clearTimeout(debounceTimer)
})

async function viewMessage(storageId: string) {
  try {
    const dto = await httpService.get<MessageDetail>(`/received/message/${storageId}`)
    detailMessage.value = dto
    detailDialogOpen.value = true
  } catch {
    alertStore.showError('Failed to load message detail')
  }
}

function confirmInboxOperation(actionName: InboxAction, generation: InboxGeneration) {
  const action = inboxActions[actionName]
  pendingAction = async () => {
    try {
      await httpService.post(
        action.path,
        createInboxOperationRequest(generation, crypto.randomUUID(), action.reason),
      )
      alertStore.showSuccess(`Inbox generation ${action.success}`)
      await loadMessages()
    } catch {
      alertStore.showError(`Failed to ${action.confirmText.toLowerCase()} inbox generation`)
    }
  }
  const props = new ConfirmDialogProps()
  props.title = action.title
  props.text = `${action.title} ${generation.consumerIdentity} generation ${generation.generation}?${
    actionName === 'delete' ? ' This action cannot be undone.' : ''
  }`
  props.confirmText = action.confirmText
  props.confirmColor = action.color
  props.icon = action.icon
  props.iconColor = action.color
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

.lane-filter {
  flex: 0 0 180px;
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

  .lane-filter {
    flex-basis: auto;
  }
}
</style>
