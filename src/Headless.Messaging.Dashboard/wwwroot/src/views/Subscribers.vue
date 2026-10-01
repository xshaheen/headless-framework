<template>
  <div class="subscribers-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Subscribers</h2>
        <v-btn
          size="small"
          variant="outlined"
          color="primary"
          prepend-icon="mdi-refresh"
          :loading="isLoading"
          @click="loadSubscribers"
        >
          Refresh
        </v-btn>
      </div>

      <TableSkeleton v-if="isLoading" :rows="6" :columns="4" />

      <div v-else-if="consumers.length === 0" class="no-data">
        <v-icon size="48" color="grey">mdi-account-group-outline</v-icon>
        <p class="mt-3">No subscribers found</p>
      </div>

      <div v-else class="groups-list">
        <v-card
          v-for="consumer in consumers"
          :key="consumer.consumerIdentity"
          class="group-card mb-4"
        >
          <v-card-title class="group-header" @click="toggleConsumer(consumer.consumerIdentity)">
            <div class="group-title-row">
              <v-icon class="mr-2">mdi-account-group</v-icon>
              <span class="group-name">{{ consumer.consumerIdentity }}</span>
              <v-chip size="x-small" color="primary" variant="tonal" class="ml-2">
                {{ consumer.childCount }} subscriber{{ consumer.childCount !== 1 ? 's' : '' }}
              </v-chip>
              <v-spacer />
              <v-icon :class="{ rotated: expandedConsumers.has(consumer.consumerIdentity) }">
                mdi-chevron-right
              </v-icon>
            </div>
          </v-card-title>

          <v-expand-transition>
            <div v-show="expandedConsumers.has(consumer.consumerIdentity)">
              <v-divider />
              <v-table density="comfortable" class="subscribers-table">
                <thead>
                  <tr>
                    <th>Message name</th>
                    <th>Lane</th>
                    <th>Implementation</th>
                    <th>Method</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(sub, index) in consumer.values" :key="index">
                    <td>{{ sub.messageName }}</td>
                    <td>{{ sub.lane }}</td>
                    <td class="text-caption">{{ sub.implName }}</td>
                    <td>
                      <code class="method-name" v-html="sub.methodEscaped"></code>
                    </td>
                  </tr>
                </tbody>
              </v-table>
            </div>
          </v-expand-transition>
        </v-card>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
defineOptions({ name: 'SubscribersView' })
import { ref, reactive, onMounted } from 'vue'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import TableSkeleton from '@/components/common/TableSkeleton.vue'

interface SubscriberValue {
  messageName: string
  lane: string
  implName: string
  methodEscaped: string
}

interface SubscriberConsumer {
  consumerIdentity: string
  childCount: number
  values: SubscriberValue[]
}

const alertStore = useAlertStore()
const isLoading = ref(false)
const consumers = ref<SubscriberConsumer[]>([])
const expandedConsumers = reactive(new Set<string>())

async function loadSubscribers() {
  isLoading.value = true
  try {
    const data = await httpService.get<SubscriberConsumer[]>('/subscriber')
    consumers.value = data || []
  } catch (error) {
    console.error('Failed to load subscribers:', error)
    alertStore.showError('Failed to load subscribers')
  } finally {
    isLoading.value = false
  }
}

function toggleConsumer(consumerIdentity: string) {
  if (expandedConsumers.has(consumerIdentity)) {
    expandedConsumers.delete(consumerIdentity)
  } else {
    expandedConsumers.add(consumerIdentity)
  }
}

onMounted(() => {
  loadSubscribers()
})
</script>

<style scoped>
.subscribers-page {
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
  margin-bottom: 20px;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #e0e0e0;
}

.no-data {
  text-align: center;
  padding: 48px;
  color: #757575;
}

.group-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.group-header {
  cursor: pointer;
  transition: background 0.2s ease;
  padding: 12px 16px !important;
}

.group-header:hover {
  background: rgba(255, 255, 255, 0.03);
}

.group-title-row {
  display: flex;
  align-items: center;
  width: 100%;
}

.group-name {
  font-size: 1rem;
  font-weight: 600;
  color: #e0e0e0;
}

.rotated {
  transform: rotate(90deg);
  transition: transform 0.3s ease;
}

.subscribers-table {
  background: transparent !important;
}

.method-name {
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.8rem;
}

:deep(.cs-type) { color: rgb(43, 145, 175); }
:deep(.cs-keyword) { color: rgb(0, 0, 255); }
:deep(.cs-string) { color: rgb(163, 21, 21); }
:deep(.cs-comment) { color: rgb(0, 128, 0); }

@media (max-width: 768px) {
  .subscribers-page {
    padding: 12px;
  }
}
</style>
