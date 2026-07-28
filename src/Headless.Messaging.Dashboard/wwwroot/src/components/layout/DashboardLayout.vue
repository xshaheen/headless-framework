<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { storeToRefs } from 'pinia'
import { useDisplay } from 'vuetify'
import AuthHeader from '../common/AuthHeader.vue'
import { useMessagingStore } from '@/stores/messagingStore'
import type { Stats } from '@/stores/messagingStore'

type BadgeColor = 'error' | 'info' | 'secondary'

interface NavLink {
  icon: string
  text: string
  path: string
  badgeKey?: keyof Stats
  badgeColor?: BadgeColor
  badgeHideWhenZero?: boolean
}

const navigationLinks: NavLink[] = [
  { icon: 'mdi-view-dashboard', text: 'Dashboard', path: '/' },
  {
    icon: 'mdi-send',
    text: 'Published',
    path: '/published',
    badgeKey: 'publishedFailed',
    badgeColor: 'error',
    badgeHideWhenZero: true,
  },
  {
    icon: 'mdi-inbox-arrow-down',
    text: 'Received',
    path: '/received',
    badgeKey: 'receivedFailed',
    badgeColor: 'error',
    badgeHideWhenZero: true,
  },
  { icon: 'mdi-alert-circle-outline', text: 'Unknown Lanes', path: '/unknown-lanes' },
  { icon: 'mdi-account-group', text: 'Subscribers', path: '/subscribers' },
  { icon: 'mdi-server-network', text: 'Nodes', path: '/nodes' },
]

const isAuthEnabled = computed(() => window.MessagingConfig?.auth?.enabled ?? false)

const messagingStore = useMessagingStore()
const { isMetaLoaded, meta, metaError, stats } = storeToRefs(messagingStore)
const providerCapabilities = computed(() => meta.value.providerCapabilities)
const isProviderCapabilitiesOpen = ref(false)
const { xs } = useDisplay()

function getNodeCookie(): string | null {
  const m = document.cookie.match(/(?:^|;\s*)messaging\.node=([^;]*)/)
  return m ? decodeURIComponent(m[1]) : null
}

const switchedNode = ref<string | null>(getNodeCookie())

function onNodeSwitched() {
  switchedNode.value = getNodeCookie()
}

onMounted(() => window.addEventListener('messaging:node-switched', onNodeSwitched))
onUnmounted(() => window.removeEventListener('messaging:node-switched', onNodeSwitched))

const router = useRouter()

function navigateToDashboard() {
  router.push('/')
}

function handleAuthLogout() {
  if (typeof window !== 'undefined') {
    window.location.reload()
  }
}
</script>

<template>
  <v-app id="inspire">
    <!-- Header -->
    <v-app-bar class="main-header">
      <div class="header-container">
        <div class="header-content">
          <div class="header-left">
            <div class="logo-container clickable" @click="navigateToDashboard">
              <img src="@/assets/logo.svg" alt="Headless Framework" class="logo-image" />
            </div>
            <div class="app-title-container clickable" @click="navigateToDashboard">
              <h1 class="app-title">
                <strong>Messaging</strong>
              </h1>
            </div>
          </div>

          <div class="header-center">
            <div class="header-divider"></div>
          </div>

          <div class="header-right">
            <div class="navigation-links">
              <v-badge
                v-for="link in navigationLinks"
                :key="link.path"
                :model-value="
                  link.badgeKey != null && !(link.badgeHideWhenZero && stats[link.badgeKey] === 0)
                "
                :content="link.badgeKey != null ? stats[link.badgeKey] : undefined"
                :color="link.badgeColor ?? 'secondary'"
                size="x-small"
                class="nav-badge"
              >
                <v-btn
                  :text="link.text"
                  :to="link.path"
                  variant="text"
                  class="nav-link"
                  :prepend-icon="link.icon"
                />
              </v-badge>
            </div>

            <div v-if="isAuthEnabled" class="auth-container">
              <AuthHeader
                :show-login-form="true"
                :show-user-info="true"
                :show-logout="true"
                @logout="handleAuthLogout"
              />
            </div>
          </div>
        </div>
      </div>
    </v-app-bar>

    <!-- Main Content -->
    <v-main class="main-content">
      <slot />
    </v-main>

    <!-- Footer -->
    <v-footer class="main-footer">
      <div class="footer-content">
        <div class="footer-badges">
          <v-chip
            v-if="meta.messaging && meta.messaging.name"
            size="x-small"
            variant="tonal"
            color="primary"
            class="footer-chip"
          >
            {{ meta.messaging.name }} v{{ meta.messaging.version }}
          </v-chip>
          <v-chip
            v-if="meta.broker && meta.broker.name"
            size="x-small"
            variant="tonal"
            color="secondary"
            class="footer-chip"
          >
            Broker: {{ meta.broker.name }}
          </v-chip>
          <v-chip
            v-if="meta.storage && meta.storage.name"
            size="x-small"
            variant="tonal"
            color="surface-variant"
            class="footer-chip"
          >
            Storage: {{ meta.storage.name }}
          </v-chip>
          <v-btn
            size="x-small"
            variant="tonal"
            color="info"
            class="footer-chip provider-capabilities-trigger"
            prepend-icon="mdi-transit-connection-variant"
            :aria-label="
              isMetaLoaded
                ? `Show provider capabilities (${providerCapabilities.length} entries)`
                : 'Show provider capabilities (loading)'
            "
            aria-haspopup="dialog"
            :aria-expanded="isProviderCapabilitiesOpen"
            @click="isProviderCapabilitiesOpen = true"
          >
            Capabilities: {{ isMetaLoaded ? providerCapabilities.length : 'loading' }}
          </v-btn>
          <v-chip
            v-if="switchedNode"
            size="x-small"
            variant="tonal"
            color="warning"
            class="footer-chip"
          >
            &#x26A1; Switched Node: {{ switchedNode }}
          </v-chip>
        </div>
        <div class="footer-copyright">2026 — <strong>Headless Framework</strong></div>
      </div>
    </v-footer>

    <v-dialog v-model="isProviderCapabilitiesOpen" :fullscreen="xs" max-width="760" scrollable>
      <v-card class="provider-capabilities" aria-live="polite">
        <v-toolbar color="transparent" class="provider-capabilities-toolbar px-2">
          <v-icon color="info" class="ml-2 mr-3">mdi-transit-connection-variant</v-icon>
          <div class="provider-capabilities-heading">
            <v-toolbar-title>Provider capabilities</v-toolbar-title>
            <p>Runtime support reported by the registered messaging providers.</p>
          </div>
          <v-spacer />
          <v-btn
            icon="mdi-close"
            variant="text"
            aria-label="Close provider capabilities"
            @click="isProviderCapabilitiesOpen = false"
          />
        </v-toolbar>

        <v-divider />

        <v-card-text v-if="!isMetaLoaded" class="provider-capabilities-state">
          <v-progress-circular indeterminate color="info" size="24" />
          <span>Loading provider capabilities…</span>
        </v-card-text>
        <v-card-text v-else-if="metaError" class="provider-capabilities-state">
          <v-icon color="error">mdi-alert-circle-outline</v-icon>
          <span>{{ metaError }}</span>
          <v-btn
            size="small"
            variant="tonal"
            prepend-icon="mdi-refresh"
            @click="messagingStore.fetchMeta()"
          >
            Retry
          </v-btn>
        </v-card-text>
        <v-card-text
          v-else-if="providerCapabilities.length === 0"
          class="provider-capabilities-state"
        >
          <v-icon color="warning">mdi-connection</v-icon>
          <span>No messaging provider capabilities are registered.</span>
        </v-card-text>
        <v-card-text v-else class="provider-capabilities-content">
          <article
            v-for="capability in providerCapabilities"
            :key="`${capability.role}:${capability.provider}`"
            class="provider-capability"
          >
            <header class="provider-capability-header">
              <div>
                <span class="provider-capability-eyebrow">{{ capability.role }} provider</span>
                <h2>{{ capability.provider }}</h2>
              </div>
            </header>

            <dl class="provider-capability-details">
              <div v-if="capability.lanes.length > 0">
                <dt>Delivery lanes</dt>
                <dd>{{ capability.lanes.join(' + ') }}</dd>
              </div>
              <div v-if="capability.role === 'Transport'">
                <dt>Topology</dt>
                <dd>
                  {{ capability.supportsIndependentLaneTopology ? 'Lane-isolated' : 'Shared' }}
                </dd>
              </div>
              <div v-if="capability.role === 'Storage'">
                <dt>Delayed scheduling</dt>
                <dd>{{ capability.supportsDelayedScheduling ? 'Supported' : 'Not supported' }}</dd>
              </div>
              <div v-if="capability.role === 'Coordination'">
                <dt>Capability</dt>
                <dd>Cluster coordination</dd>
              </div>
            </dl>
          </article>
        </v-card-text>
      </v-card>
    </v-dialog>
  </v-app>
</template>

<style scoped>
#inspire {
  --dashboard-shell-max-width: 1240px;
  --dashboard-shell-padding-x: clamp(16px, 2.4vw, 28px);
  --dashboard-card-padding: 16px;
  --dashboard-control-gap: 12px;
}

.main-header {
  background: rgba(33, 33, 33, 0.95) !important;
  backdrop-filter: blur(20px) !important;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1) !important;
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.3) !important;
  transition: all 0.3s ease !important;
  padding: 0 !important;
}

.main-header:hover {
  background: rgba(33, 33, 33, 0.98) !important;
  box-shadow: 0 6px 25px rgba(0, 0, 0, 0.4) !important;
}

.header-container {
  width: 100%;
  max-width: var(--dashboard-shell-max-width);
  margin: 0 auto;
  padding: 0 var(--dashboard-shell-padding-x);
}

.header-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 60px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-shrink: 0;
}

.logo-container {
  display: flex;
  align-items: center;
  padding: 8px 0;
  cursor: pointer;
  transition: all 0.3s ease;
}

.logo-container:hover {
  transform: translateY(-1px);
}

.logo-image {
  height: 40px;
  width: auto;
  transition: transform 0.3s ease;
}

.logo-container:hover .logo-image {
  transform: scale(1.05);
}

.app-title-container {
  display: flex;
  align-items: center;
  cursor: pointer;
  transition: all 0.3s ease;
  padding: 8px 12px;
  border-radius: 8px;
}

.app-title-container:hover {
  background: rgba(255, 255, 255, 0.1);
  transform: translateY(-1px);
}

.clickable {
  user-select: none;
}

.app-title {
  color: #e0e0e0 !important;
  font-size: 1.5rem !important;
  font-weight: 700 !important;
  letter-spacing: -0.5px !important;
  margin: 0 !important;
}

.header-center {
  flex: 1;
  display: flex;
  justify-content: center;
  align-items: center;
}

.header-divider {
  width: 1px;
  height: 32px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 1px;
}

.header-right {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-shrink: 0;
  gap: 12px;
}

.auth-container {
  display: flex;
  align-items: center;
  margin-left: 16px;
  padding-left: 16px;
  border-left: 1px solid rgba(255, 255, 255, 0.1);
}

.navigation-links {
  display: flex;
  align-items: center;
  gap: 6px;
}

.nav-link {
  color: #bdbdbd !important;
  font-weight: 500 !important;
  text-transform: none !important;
  letter-spacing: 0.5px !important;
  border-radius: 8px !important;
  transition: all 0.3s ease !important;
  padding: 6px 12px !important;
}

.nav-link:hover {
  color: #e0e0e0 !important;
  background: rgba(255, 255, 255, 0.1) !important;
  transform: translateY(-1px) !important;
}

.nav-link.v-btn--active {
  color: var(--v-theme-primary) !important;
  background: rgba(var(--v-theme-primary), 0.1) !important;
}

.main-content {
  flex: 1 0 auto;
}

.main-footer {
  background: rgba(33, 33, 33, 0.95) !important;
  backdrop-filter: blur(20px) !important;
  border-top: 1px solid rgba(255, 255, 255, 0.1) !important;
  box-shadow: 0 -2px 12px rgba(0, 0, 0, 0.3) !important;
  padding: 0 !important;
  height: 40px !important;
  min-height: 40px !important;
  max-height: 40px !important;
  flex: 0 0 40px !important;
}

.footer-content {
  width: 100%;
  min-width: 0;
  max-width: var(--dashboard-shell-max-width);
  margin: 0 auto;
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  height: 40px;
  padding: 0 var(--dashboard-shell-padding-x);
  gap: 12px;
}

.footer-badges {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: nowrap;
  overflow: hidden;
}

.footer-chip {
  font-size: 0.7rem !important;
  height: 20px !important;
  white-space: nowrap;
}

.provider-capabilities-trigger {
  min-width: 0 !important;
  padding: 0 8px !important;
  letter-spacing: normal !important;
  text-transform: none !important;
}

.provider-capabilities {
  max-height: min(720px, calc(100dvh - 48px));
  display: flex;
  flex-direction: column;
}

.provider-capabilities-toolbar {
  min-height: 72px;
  flex-shrink: 0;
}

.provider-capabilities-heading {
  min-width: 0;
}

.provider-capabilities-heading :deep(.v-toolbar-title) {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 600;
  line-height: 1.35;
}

.provider-capabilities-heading p {
  margin: 2px 0 0;
  color: #9e9e9e;
  font-size: 0.78rem;
  line-height: 1.35;
}

.provider-capabilities-state {
  min-height: 132px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-wrap: wrap;
  gap: 12px;
  text-align: center;
}

.provider-capabilities-content {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(280px, 100%), 1fr));
  align-content: start;
  gap: 14px;
  overflow-y: auto;
}

.provider-capability {
  min-width: 0;
  align-self: start;
  padding: 18px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.025);
}

.provider-capability-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding-bottom: 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.provider-capability-eyebrow {
  display: block;
  margin-bottom: 3px;
  color: #8c9eff;
  font-size: 0.68rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.provider-capability h2 {
  margin: 0;
  color: #f5f5f5;
  font-size: 1.2rem;
  font-weight: 600;
}

.provider-capability-details {
  margin: 0;
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
  padding-top: 16px;
}

.provider-capability-details div {
  min-width: 0;
}

.provider-capability-details dt {
  margin-bottom: 5px;
  color: #9e9e9e;
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.035em;
  text-transform: uppercase;
}

.provider-capability-details dd {
  margin: 0;
  color: #eeeeee;
  font-size: 0.9rem;
  font-weight: 500;
  overflow-wrap: anywhere;
}

.footer-copyright {
  color: #bdbdbd !important;
  font-size: 0.8rem !important;
  font-weight: 500 !important;
  white-space: nowrap;
  flex-shrink: 0;
}

.footer-copyright strong {
  color: #e0e0e0 !important;
  font-weight: 600 !important;
}

@media (max-width: 768px) {
  .header-content {
    height: auto;
    min-height: 60px;
    padding: 10px 0;
  }

  .header-left {
    flex-direction: column;
    gap: 12px;
    align-items: center;
  }

  .header-center {
    display: none;
  }

  .header-right {
    justify-content: center;
    flex-direction: column;
    gap: 12px;
  }

  .navigation-links {
    flex-wrap: wrap;
    justify-content: center;
  }

  .auth-container {
    margin-left: 0;
    padding-left: 0;
    border-left: none;
    border-top: 1px solid rgba(255, 255, 255, 0.1);
    padding-top: 12px;
    width: 100%;
    justify-content: center;
  }

  .main-footer {
    height: auto !important;
    min-height: 40px !important;
    max-height: none !important;
    flex-basis: auto !important;
  }

  .footer-content {
    height: auto;
    min-height: 40px;
    padding-top: 8px;
    padding-bottom: 8px;
  }

  .footer-badges {
    flex: 1 1 100%;
    flex-wrap: wrap;
    overflow: visible;
  }

  .footer-copyright {
    display: none;
  }
}

@media (max-width: 480px) {
  .header-container {
    padding: 0 12px;
  }

  .app-title {
    font-size: 1.25rem !important;
  }

  .logo-image {
    height: 32px;
  }
}

@media (max-width: 599.98px) {
  .provider-capabilities {
    max-height: none;
    height: 100%;
    border-radius: 0 !important;
  }

  .provider-capabilities-toolbar {
    min-height: 76px;
  }

  .provider-capabilities-heading p {
    max-width: 240px;
  }

  .provider-capability-header {
    align-items: flex-start;
    flex-direction: column;
    gap: 10px;
  }

  .provider-capability-details {
    grid-template-columns: 1fr;
    gap: 14px;
  }
}
</style>
