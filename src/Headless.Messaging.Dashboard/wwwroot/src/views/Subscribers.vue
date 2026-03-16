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

      <div v-else-if="groups.length === 0" class="no-data">
        <v-icon size="48" color="grey">mdi-account-group-outline</v-icon>
        <p class="mt-3">No subscribers found</p>
      </div>

      <div v-else class="groups-list">
        <v-card v-for="group in groups" :key="group.group" class="group-card mb-4">
          <v-card-title class="group-header" @click="toggleGroup(group.group)">
            <div class="group-title-row">
              <v-icon class="mr-2">mdi-account-group</v-icon>
              <span class="group-name">{{ group.group }}</span>
              <v-chip size="x-small" color="primary" variant="tonal" class="ml-2">
                {{ group.childCount }} subscriber{{ group.childCount !== 1 ? 's' : '' }}
              </v-chip>
              <v-spacer />
              <v-icon :class="{ rotated: expandedGroups.has(group.group) }">
                mdi-chevron-down
              </v-icon>
            </div>
          </v-card-title>

          <v-expand-transition>
            <div v-show="expandedGroups.has(group.group)">
              <v-divider />
              <v-table density="comfortable" class="subscribers-table">
                <thead>
                  <tr>
                    <th>Topic</th>
                    <th>Implementation</th>
                    <th>Method</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(sub, index) in group.values" :key="index">
                    <td>{{ sub.topic }}</td>
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
import { ref, reactive, onMounted } from 'vue'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import TableSkeleton from '@/components/common/TableSkeleton.vue'

interface SubscriberValue {
  topic: string
  implName: string
  methodEscaped: string
}

interface SubscriberGroup {
  group: string
  childCount: number
  values: SubscriberValue[]
}

const alertStore = useAlertStore()
const isLoading = ref(false)
const groups = ref<SubscriberGroup[]>([])
const expandedGroups = reactive(new Set<string>())

async function loadSubscribers() {
  isLoading.value = true
  try {
    const data = await httpService.get<SubscriberGroup[]>('/subscriber')
    groups.value = data || []
    // Expand all groups by default
    groups.value.forEach((g) => expandedGroups.add(g.group))
  } catch (error) {
    console.error('Failed to load subscribers:', error)
    alertStore.showError('Failed to load subscribers')
  } finally {
    isLoading.value = false
  }
}

function toggleGroup(group: string) {
  if (expandedGroups.has(group)) {
    expandedGroups.delete(group)
  } else {
    expandedGroups.add(group)
  }
}

onMounted(() => {
  loadSubscribers()
})
</script>

<style scoped>
.subscribers-page {
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
  transform: rotate(180deg);
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
