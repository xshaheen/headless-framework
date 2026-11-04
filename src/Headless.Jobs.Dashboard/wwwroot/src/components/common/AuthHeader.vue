<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'

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

// Signs out of the dashboard only (in host mode the application session stays) and shows the signed-out page,
// which signs in again only when the user asks.
const handleLogout = async () => {
  await authStore.logout()
  await router.push({ name: 'Login' })
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

/* Login Form Styles */
.login-prompt {
  display: flex;
  align-items: center;
}

.login-btn {
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 8px;
  transition: all 0.3s ease;
}

.login-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 400px;
}

.auth-notice {
  margin-bottom: 8px;
}

.info-alert {
  font-size: 0.875rem;
}

.form-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.username-field,
.password-field,
.api-key-field {
  flex: 1;
  min-width: 120px;
}

.submit-btn {
  font-weight: 600;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 8px;
  transition: all 0.3s ease;
}

.submit-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.cancel-btn {
  font-weight: 500;
  text-transform: none;
  color: #757575;
}

.error-message {
  margin-top: 4px;
}

.error-alert {
  cursor: pointer;
  transition: all 0.3s ease;
}

.error-alert:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
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

/* Responsive Design */
@media (max-width: 768px) {
  .login-form {
    min-width: 300px;
  }
  
  .form-row {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }
  
  .username-field,
  .password-field,
  .api-key-field {
    min-width: auto;
  }
}

@media (max-width: 480px) {
  .login-form {
    min-width: 250px;
  }
  
  .auth-header {
    gap: 12px;
  }
}
</style> 