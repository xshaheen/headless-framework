<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useAlert } from '@/composables/useAlert'

// Props
interface Props {
  showUserInfo?: boolean
  showLogout?: boolean
}

withDefaults(defineProps<Props>(), {
  showUserInfo: true,
  showLogout: true
})

const router = useRouter()
const authStore = useAuthStore()
const { showError } = useAlert()

// Signs out of the dashboard only (in host mode the application session stays) and shows the signed-out page,
// which signs in again only when the user asks. A sign-out the server refused leaves the user where they are.
const handleLogout = async () => {
  try {
    await authStore.logout()
  } catch (error) {
    console.error('Sign-out failed:', error)
    showError('Sign-out failed. You are still signed in; please try again.')
    return
  }

  await router.push({ name: 'Login', query: { signedOut: '1' } })
}
</script>

<template>
  <div class="auth-header">
    <div v-if="authStore.isLoggedIn && showUserInfo" class="auth-section">
      <div class="user-info">
        <v-icon class="user-icon">mdi-account-circle</v-icon>
        <span class="username">{{ authStore.username }}</span>
        <v-chip v-if="!authStore.canOperate" size="x-small" variant="tonal" class="ml-2">View only</v-chip>
        <v-divider vertical class="divider" />
        <v-btn
          v-if="showLogout"
          color="error"
          variant="text"
          size="small"
          @click="handleLogout"
          class="logout-btn"
        >
          <v-icon start>mdi-logout</v-icon>
          Sign out
        </v-btn>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auth-header {
  display: flex;
  align-items: center;
  gap: 16px;
}

.auth-section {
  display: flex;
  align-items: center;
}

/* User Info Styles */
.user-info {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.user-icon {
  color: #4caf50;
  font-size: 1.25rem;
}

.username {
  font-weight: 600;
  color: #e0e0e0;
  font-size: 0.875rem;
}

.divider {
  border-color: rgba(255, 255, 255, 0.2);
  height: 20px;
}

.logout-btn {
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 6px;
  transition: all 0.3s ease;
}

.logout-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

@media (max-width: 480px) {
  .auth-header {
    gap: 12px;
  }
}
</style>
