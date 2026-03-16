<template>
  <div class="nodes-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Nodes</h2>
        <v-btn
          size="small"
          variant="outlined"
          color="primary"
          prepend-icon="mdi-refresh"
          :loading="isLoading"
          @click="loadNodes"
        >
          Refresh
        </v-btn>
      </div>

      <TableSkeleton v-if="isLoading" :rows="4" :columns="5" />

      <div v-else-if="nodes.length === 0" class="no-data">
        <v-icon size="48" color="grey">mdi-server-network-off</v-icon>
        <p class="mt-3">No nodes found</p>
      </div>

      <v-card v-else class="nodes-card">
        <v-table density="comfortable" class="nodes-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Address</th>
              <th>Port</th>
              <th>Tags</th>
              <th>Latency</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="node in nodes" :key="node.name">
              <td class="node-name">
                <v-icon size="small" class="mr-2" color="primary">mdi-server</v-icon>
                {{ node.name }}
              </td>
              <td>{{ node.address }}</td>
              <td>{{ node.port }}</td>
              <td>
                <v-chip
                  v-for="tag in parseTags(node.tags)"
                  :key="tag"
                  size="x-small"
                  color="primary"
                  variant="tonal"
                  class="mr-1"
                >
                  {{ tag }}
                </v-chip>
              </td>
              <td>
                <span v-if="pingResults[node.name] === undefined" class="text-medium-emphasis">
                  --
                </span>
                <span v-else-if="pingResults[node.name] === -1" class="text-error"> Failed </span>
                <v-chip v-else size="x-small" :color="getLatencyColor(pingResults[node.name])" variant="tonal">
                  {{ pingResults[node.name] }}ms
                </v-chip>
              </td>
              <td>
                <v-btn
                  size="x-small"
                  variant="text"
                  color="primary"
                  prepend-icon="mdi-access-point-network"
                  :loading="pingingNodes.has(node.name)"
                  @click="pingNode(node)"
                >
                  Ping
                </v-btn>
                <v-btn
                  size="x-small"
                  variant="text"
                  color="warning"
                  prepend-icon="mdi-swap-horizontal"
                  @click="switchToNode(node)"
                >
                  Switch
                </v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>
      </v-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { httpService } from '@/services/http'
import { useAlertStore } from '@/stores/alertStore'
import TableSkeleton from '@/components/common/TableSkeleton.vue'

interface NodeInfo {
  name: string
  address: string
  port: number
  tags: string
}

const alertStore = useAlertStore()
const isLoading = ref(false)
const nodes = ref<NodeInfo[]>([])
const pingResults = reactive<Record<string, number>>({})
const pingingNodes = reactive(new Set<string>())

async function loadNodes() {
  isLoading.value = true
  try {
    const data = await httpService.get<NodeInfo[]>('/nodes')
    nodes.value = data || []
  } catch (error) {
    console.error('Failed to load nodes:', error)
    alertStore.showError('Failed to load nodes')
  } finally {
    isLoading.value = false
  }
}

function parseTags(tags: string): string[] {
  if (!tags) return []
  try {
    const parsed = JSON.parse(tags)
    if (Array.isArray(parsed)) return parsed
    return [String(parsed)]
  } catch {
    return tags
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean)
  }
}

async function pingNode(node: NodeInfo) {
  pingingNodes.add(node.name)
  try {
    const endpoint = `${node.address}:${node.port}`
    const result = await httpService.get<string>(`/ping?endpoint=${encodeURIComponent(endpoint)}`)
    const latency = parseInt(String(result), 10)
    pingResults[node.name] = isNaN(latency) ? -1 : latency
  } catch (error) {
    pingResults[node.name] = -1
    console.error('Ping failed:', error)
  } finally {
    pingingNodes.delete(node.name)
  }
}

function switchToNode(node: NodeInfo) {
  const endpoint = `${node.address}:${node.port}`
  document.cookie = `messaging.currentnode=${encodeURIComponent(endpoint)};path=/;max-age=86400`
  alertStore.showSuccess(`Switched to node: ${node.name}`)
}

function getLatencyColor(latency: number): string {
  if (latency < 100) return 'success'
  if (latency < 300) return 'warning'
  return 'error'
}

onMounted(() => {
  loadNodes()
})
</script>

<style scoped>
.nodes-page {
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

.nodes-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.nodes-table {
  background: transparent !important;
}

.node-name {
  display: flex;
  align-items: center;
  font-weight: 600;
  color: #e0e0e0;
}

.text-error {
  color: #f44336 !important;
}

@media (max-width: 768px) {
  .nodes-page {
    padding: 12px;
  }
}
</style>
