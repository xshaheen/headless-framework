<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { storeToRefs } from 'pinia'
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
  { icon: 'mdi-send', text: 'Published', path: '/published', badgeKey: 'publishedFailed', badgeColor: 'error', badgeHideWhenZero: true },
  { icon: 'mdi-inbox-arrow-down', text: 'Received', path: '/received', badgeKey: 'receivedFailed', badgeColor: 'error', badgeHideWhenZero: true },
  { icon: 'mdi-account-group', text: 'Subscribers', path: '/subscribers', badgeKey: 'subscribers', badgeColor: 'info' },
  { icon: 'mdi-server-network', text: 'Nodes', path: '/nodes', badgeKey: 'servers', badgeColor: 'secondary' },
]

const isAuthEnabled = computed(() => window.MessagingConfig?.auth?.enabled ?? false)

const messagingStore = useMessagingStore()
const { meta, stats } = storeToRefs(messagingStore)

function getNodeCookie(): string | null {
  const m = document.cookie.match(/(?:^|;\s*)messaging\.node=([^;]*)/)
  return m ? decodeURIComponent(m[1]) : null
}

const switchedNode = computed(() => getNodeCookie())

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
                :model-value="link.badgeKey != null && !(link.badgeHideWhenZero && stats[link.badgeKey] === 0)"
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
            {{ meta.broker.name }}
          </v-chip>
          <v-chip
            v-if="meta.storage && meta.storage.name"
            size="x-small"
            variant="tonal"
            color="surface-variant"
            class="footer-chip"
          >
            {{ meta.storage.name }}
          </v-chip>
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
        <div class="footer-copyright">2025 — <strong>Headless Framework</strong></div>
      </div>
    </v-footer>
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
</style>
