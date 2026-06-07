<script lang="ts" setup>
import { jobsService } from '@/http/services/jobsService'
import JobNotificationHub, { methodName } from '@/hub/jobNotificationHub'
import { onMounted, onUnmounted, ref, type Ref } from 'vue'
import type { GetLiveNode } from '@/http/services/types/jobsService.types'

interface NodeState {
  identity: string
  state: string
  role?: string
  lastBeat?: string
}

interface NodeDelta {
  identity: string
  state: string
  eventType: string
}

const getLiveNodes = jobsService.getLiveNodes()
const nodes: Ref<NodeState[]> = ref([])
const loading = ref(true)

// Apply a single membership delta pushed over SignalR: upsert by identity; a Dead node is kept so the
// panel renders the terminal state rather than silently dropping it.
function applyDelta(delta: NodeDelta): void {
  const existing = nodes.value.find((n) => n.identity === delta.identity)
  if (existing) {
    existing.state = delta.state
  } else {
    nodes.value.push({ identity: delta.identity, state: delta.state })
  }
}

onMounted(async () => {
  await getLiveNodes
    .requestAsync()
    .then((res) => {
      nodes.value = (res as GetLiveNode[]).map((n) => ({
        identity: n.identity,
        state: n.state,
        role: n.role,
        lastBeat: n.lastBeat,
      }))
    })
    .finally(() => {
      loading.value = false
    })

  JobNotificationHub.onReceiveNodesUpdate((delta: NodeDelta) => {
    applyDelta(delta)
  })
})

onUnmounted(() => {
  JobNotificationHub.stopReceiver(methodName.onReceiveNodesUpdate)
})

function stateClass(state: string): string {
  switch (state) {
    case 'Alive':
      return 'node-state-alive'
    case 'Suspected':
      return 'node-state-suspected'
    case 'Dead':
      return 'node-state-dead'
    default:
      return 'node-state-unknown'
  }
}
</script>

<template>
  <div class="content-card nodes-card">
    <div class="card-header">
      <h2 class="card-title">
        <v-icon class="title-icon" color="primary">mdi-lan-connect</v-icon>
        Live Nodes
      </h2>
      <p class="card-subtitle">{{ nodes.length }} cluster nodes</p>
    </div>

    <div class="nodes-list">
      <template v-if="loading">
        <div v-for="i in 3" :key="i" class="node-item">
          <div class="skeleton-circle node-dot-skeleton"></div>
          <div class="node-info">
            <div class="skeleton-text node-name-skeleton"></div>
            <div class="skeleton-text node-meta-skeleton"></div>
          </div>
        </div>
      </template>
      <template v-else-if="nodes.length > 0">
        <div v-for="node in nodes" :key="node.identity" class="node-item">
          <span class="node-dot" :class="stateClass(node.state)"></span>
          <div class="node-info">
            <span class="node-name">
              {{ node.identity }}
              <span v-if="node.role" class="node-role-pill">{{ node.role }}</span>
            </span>
            <span class="node-meta">
              <span class="node-state-label" :class="stateClass(node.state)">{{ node.state }}</span>
              <span v-if="node.lastBeat" class="node-lastbeat">· {{ node.lastBeat }}</span>
            </span>
          </div>
        </div>
      </template>
      <template v-else>
        <div class="nodes-empty-state">
          <v-icon class="nodes-empty-icon" color="primary">mdi-lan-disconnect</v-icon>
          <span class="nodes-empty-title">No coordinated nodes</span>
          <span class="nodes-empty-copy">
            Live nodes appear here when a coordination provider is configured for the durable job store.
          </span>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.nodes-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.node-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  transition: all 0.2s ease;
}

.node-item:hover {
  background: rgba(255, 255, 255, 0.1);
  transform: translateX(2px);
}

.node-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.node-state-alive {
  background: #4caf50;
  color: #4caf50;
}

.node-state-suspected {
  background: #ffb74d;
  color: #ffb74d;
}

.node-state-dead {
  background: #f44336;
  color: #f44336;
}

.node-state-unknown {
  background: #9e9e9e;
  color: #9e9e9e;
}

.node-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.node-name {
  font-weight: 600;
  color: #e0e0e0;
  font-size: 0.875rem;
  display: flex;
  align-items: center;
  gap: 8px;
}

.node-role-pill {
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 2px 8px;
  border-radius: 10px;
  background: rgba(100, 181, 246, 0.2);
  color: #64b5f6;
}

.node-meta {
  font-size: 0.75rem;
  color: #bdbdbd;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 6px;
}

.node-state-label {
  font-weight: 600;
}

.node-lastbeat {
  color: #9e9e9e;
}

.nodes-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 180px;
  padding: 20px 16px;
  border: 1px dashed rgba(255, 255, 255, 0.12);
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.03);
  text-align: center;
}

.nodes-empty-icon {
  font-size: 28px;
}

.nodes-empty-title {
  font-size: 0.95rem;
  font-weight: 600;
  color: #e0e0e0;
}

.nodes-empty-copy {
  max-width: 320px;
  font-size: 0.8rem;
  line-height: 1.45;
  color: #9e9e9e;
}

.skeleton-text {
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.1) 25%,
    rgba(255, 255, 255, 0.2) 37%,
    rgba(255, 255, 255, 0.1) 63%
  );
  background-size: 400px 100%;
  animation: skeleton-loading 1.4s ease-in-out infinite;
  border-radius: 4px;
}

.skeleton-circle {
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.1) 25%,
    rgba(255, 255, 255, 0.2) 37%,
    rgba(255, 255, 255, 0.1) 63%
  );
  background-size: 400px 100%;
  animation: skeleton-loading 1.4s ease-in-out infinite;
  border-radius: 50%;
}

.node-dot-skeleton {
  width: 10px;
  height: 10px;
}

.node-name-skeleton {
  height: 14px;
  width: 140px;
  margin-bottom: 4px;
}

.node-meta-skeleton {
  height: 12px;
  width: 70px;
}

@keyframes skeleton-loading {
  0% {
    background-position: -200px 0;
  }
  100% {
    background-position: calc(200px + 100%) 0;
  }
}
</style>
