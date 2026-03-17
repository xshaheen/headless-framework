<template>
  <div class="nodes-page">
    <div class="page-content">
      <div class="page-header">
        <h2 class="page-title">Nodes</h2>
        <div class="header-actions">
          <v-select
            v-if="namespaces.length > 0"
            v-model="selectedNamespace"
            :items="namespaces"
            label="Namespace"
            density="compact"
            variant="outlined"
            hide-details
            class="ns-select"
            @update:model-value="onNamespaceChange"
          />
          <v-btn
            size="small"
            variant="outlined"
            color="secondary"
            prepend-icon="mdi-speedometer"
            :loading="isPingingAll"
            :disabled="nodes.length === 0 || isLoading"
            @click="pingAll"
          >
            Ping All
          </v-btn>
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
      </div>

      <TableSkeleton v-if="isLoading" :rows="4" :columns="5" />

      <div v-else-if="nodes.length === 0" class="no-data">
        <v-icon size="48" color="grey">mdi-server-network-off</v-icon>
        <p class="mt-3 text-medium-emphasis">No nodes discovered</p>
        <p class="mt-1 text-caption text-disabled">
          Node discovery requires a service registry (Consul or Kubernetes).
          <br />
          Configure with <code>.UseConsulDiscovery()</code> or <code>.UseK8sDiscovery()</code> in your dashboard setup.
        </p>
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
            <tr
              v-for="node in nodes"
              :key="node.name"
              :class="{ 'active-node': isActiveNode(node) }"
            >
              <td class="node-name">
                <v-icon size="small" class="mr-2" color="primary">mdi-server</v-icon>
                {{ node.name }}
                <v-chip
                  v-if="isActiveNode(node)"
                  size="x-small"
                  color="primary"
                  variant="flat"
                  class="ml-2"
                >
                  active
                </v-chip>
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

// --- Cookie helpers ---
const getCookie = (name: string): string | null => {
  const m = document.cookie.match(new RegExp('(?:^|;\\s*)' + name.replaceAll('.', '\\.') + '=([^;]*)'))
  return m ? decodeURIComponent(m[1]) : null
}

const setCookie = (name: string, value: string) => {
  document.cookie = `${name}=${encodeURIComponent(value)};path=/`
}

// --- State ---
const alertStore = useAlertStore()
const isLoading = ref(false)
const nodes = ref<NodeInfo[]>([])
const pingResults = reactive<Record<string, number>>({})
const pingingNodes = reactive(new Set<string>())
const isPingingAll = ref(false)
let loadGeneration = 0

const namespaces = ref<string[]>([])
const selectedNamespace = ref<string | null>(getCookie('messaging.node.ns'))

// --- Active node ---
function getActiveNodeEndpoint(): string | null {
  return getCookie('messaging.node')
}

function isActiveNode(node: NodeInfo): boolean {
  const active = getActiveNodeEndpoint()
  if (!active) return false
  const nodeEndpoint = `${node.address}:${node.port}`
  return active === nodeEndpoint
}

// --- Namespace loading ---
async function loadNamespaces() {
  try {
    const data = await httpService.get<string[]>('/list-ns')
    namespaces.value = data || []
  } catch {
    namespaces.value = []
  }
}

async function onNamespaceChange(ns: string) {
  selectedNamespace.value = ns
  setCookie('messaging.node.ns', ns)
  await loadNodesByNamespace(ns)
}

async function loadNodesByNamespace(ns: string) {
  const generation = ++loadGeneration
  isLoading.value = true
  try {
    const data = await httpService.get<NodeInfo[]>(`/list-svc/${encodeURIComponent(ns)}`)
    if (generation !== loadGeneration) return
    nodes.value = data || []
  } catch (error) {
    if (generation !== loadGeneration) return
    console.error('Failed to load nodes for namespace:', error)
    alertStore.showError('Failed to load nodes for namespace')
  } finally {
    if (generation === loadGeneration) isLoading.value = false
  }
}

// --- Node loading ---
async function loadNodes() {
  if (namespaces.value.length > 0 && selectedNamespace.value) {
    await loadNodesByNamespace(selectedNamespace.value)
    return
  }

  const generation = ++loadGeneration
  isLoading.value = true
  try {
    const data = await httpService.get<NodeInfo[]>('/nodes')
    if (generation !== loadGeneration) return
    nodes.value = data || []
  } catch (error) {
    if (generation !== loadGeneration) return
    console.error('Failed to load nodes:', error)
    alertStore.showError('Failed to load nodes')
  } finally {
    if (generation === loadGeneration) isLoading.value = false
  }
}

// --- Helpers ---
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

// --- Ping ---
async function pingNode(node: NodeInfo) {
  if (pingingNodes.has(node.name)) return
  pingingNodes.add(node.name)
  try {
    const hasScheme = node.address.startsWith('http://') || node.address.startsWith('https://')
    const endpoint = hasScheme ? `${node.address}:${node.port}` : `http://${node.address}:${node.port}`
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

async function pingAll() {
  if (nodes.value.length === 0) return
  isPingingAll.value = true
  try {
    for (const node of nodes.value) {
      await pingNode(node)
    }
  } finally {
    isPingingAll.value = false
  }
}

// --- Switch node ---
function switchToNode(node: NodeInfo) {
  const endpoint = `${node.address}:${node.port}`
  setCookie('messaging.node', endpoint)
  if (selectedNamespace.value) {
    setCookie('messaging.node.ns', selectedNamespace.value)
  }
  alertStore.showSuccess(`Switched to node: ${node.name}`)
  window.dispatchEvent(new Event('messaging:node-switched'))
}

function getLatencyColor(latency: number): string {
  if (latency < 100) return 'success'
  if (latency < 300) return 'warning'
  return 'error'
}

// --- Init ---
onMounted(async () => {
  await loadNamespaces()

  if (namespaces.value.length > 0) {
    // Restore previously selected namespace or default to first
    if (!selectedNamespace.value || !namespaces.value.includes(selectedNamespace.value)) {
      selectedNamespace.value = namespaces.value[0]
      setCookie('messaging.node.ns', selectedNamespace.value)
    }
    await loadNodesByNamespace(selectedNamespace.value)
  } else {
    await loadNodes()
  }
})
</script>

<style scoped>
.nodes-page {
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

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ns-select {
  min-width: 160px;
  max-width: 240px;
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

.active-node {
  background: rgba(var(--v-theme-primary), 0.1);
  border-left: 3px solid rgb(var(--v-theme-primary));
}

@media (max-width: 768px) {
  .nodes-page {
    padding: 12px;
  }

  .header-actions {
    flex-wrap: wrap;
  }

  .ns-select {
    min-width: 120px;
  }
}
</style>
