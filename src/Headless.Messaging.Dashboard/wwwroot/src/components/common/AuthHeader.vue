<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useAuth } from '../../composables/useAuth'
import { useAuthStore } from '@/stores/authStore'
import { useAlert } from '@/composables/useAlert'

interface Props {
  showLoginForm?: boolean
  showUserInfo?: boolean
  showLogout?: boolean
}

withDefaults(defineProps<Props>(), {
  showLoginForm: true,
  showUserInfo: true,
  showLogout: true,
})

const emit = defineEmits<{
  login: [success: boolean]
  logout: []
}>()

const authStore = useAuthStore()
const { isAuthenticated, username, isLoading, errorMessage, clearError } = useAuth()
const { showSuccess, showError, showInfo } = useAlert()

const isLoginFormVisible = ref(false)
const localUsername = ref('')
const localPassword = ref('')
const localApiKey = ref('')

const requiresAuth = computed(() => {
  return window.MessagingConfig?.auth?.enabled || false
})

const authMode = computed(() => {
  const mode = window.MessagingConfig?.auth?.mode
  if (mode === 'basic') return 'basic'
  if (mode === 'apikey') return 'apikey'
  if (mode === 'host') return 'host'
  return 'none'
})

const shouldShowLoginForm = computed(() => {
  if (requiresAuth.value && !isAuthenticated.value) {
    return true
  }
  return isLoginFormVisible.value
})

const toggleLoginForm = () => {
  if (requiresAuth.value) return
  isLoginFormVisible.value = !isLoginFormVisible.value
}

const handleLogin = async () => {
  try {
    if (authMode.value === 'basic') {
      authStore.credentials.username = localUsername.value
      authStore.credentials.password = localPassword.value
    } else if (authMode.value === 'apikey') {
      authStore.credentials.apiKey = localApiKey.value
    }

    const success = await authStore.login()

    if (success) {
      isLoginFormVisible.value = false
      showSuccess('Login successful!')
      emit('login', true)
      localUsername.value = ''
      localPassword.value = ''
      localApiKey.value = ''
    } else {
      showError(authStore.errorMessage || 'Login failed')
    }
  } catch (error) {
    console.error('Login error:', error)
    showError('Login failed. Please try again.')
  }
}

const handleLogout = async () => {
  try {
    authStore.logout()
    showInfo('Logged out successfully')
    emit('logout')
  } catch (error) {
    console.error('Logout error:', error)
    showError('Logout failed')
    emit('logout')
  }
}

onMounted(() => {
  if (requiresAuth.value && !isAuthenticated.value) {
    isLoginFormVisible.value = true
  }
})

watch(isAuthenticated, (newValue) => {
  if (newValue) {
    isLoginFormVisible.value = false
  } else if (requiresAuth.value) {
    isLoginFormVisible.value = true
  }
})
</script>

<template>
  <div class="auth-header">
    <div v-if="!isAuthenticated && showLoginForm" class="auth-section">
      <div v-if="!shouldShowLoginForm" class="login-prompt">
        <v-btn
          color="primary"
          variant="outlined"
          size="small"
          class="login-btn"
          @click="toggleLoginForm"
        >
          <v-icon start>mdi-login</v-icon>
          Login
        </v-btn>
      </div>

      <div v-else class="login-form">
        <div class="form-row">
          <template v-if="authMode === 'basic'">
            <v-text-field
              v-model="localUsername"
              label="Username"
              variant="outlined"
              density="compact"
              size="small"
              class="username-field"
              :disabled="isLoading"
              @keyup.enter="handleLogin"
            />
            <v-text-field
              v-model="localPassword"
              label="Password"
              type="password"
              variant="outlined"
              density="compact"
              size="small"
              class="password-field"
              :disabled="isLoading"
              @keyup.enter="handleLogin"
            />
          </template>

          <template v-else-if="authMode === 'apikey'">
            <v-text-field
              v-model="localApiKey"
              label="API Key"
              type="password"
              variant="outlined"
              density="compact"
              size="small"
              class="api-key-field"
              :disabled="isLoading"
              placeholder="Enter your API key"
              @keyup.enter="handleLogin"
            />
          </template>

          <v-btn
            color="primary"
            variant="elevated"
            size="small"
            class="submit-btn"
            :loading="isLoading"
            :disabled="
              (authMode === 'basic' && (!localUsername || !localPassword)) ||
              (authMode === 'apikey' && !localApiKey)
            "
            @click="handleLogin"
          >
            <v-icon start>mdi-login</v-icon>
            Login
          </v-btn>
          <v-btn
            v-if="!requiresAuth"
            variant="text"
            size="small"
            class="cancel-btn"
            :disabled="isLoading"
            @click="toggleLoginForm"
          >
            Cancel
          </v-btn>
        </div>

        <div v-if="errorMessage" class="error-message">
          <v-alert type="error" variant="tonal" density="compact" class="error-alert" @click="clearError">
            {{ errorMessage }}
          </v-alert>
        </div>
      </div>
    </div>

    <div v-if="isAuthenticated && showUserInfo" class="auth-section">
      <div class="user-info">
        <v-icon class="user-icon">mdi-account-circle</v-icon>
        <span class="username">{{ username }}</span>
        <v-divider vertical class="divider" />
        <v-btn
          v-if="showLogout"
          color="error"
          variant="text"
          size="small"
          class="logout-btn"
          @click="handleLogout"
        >
          <v-icon start>mdi-logout</v-icon>
          Logout
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

.login-btn {
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.5px;
  border-radius: 8px;
  transition: all 0.3s ease;
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 400px;
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
  border-radius: 8px;
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
}

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
  border-radius: 6px;
}

@media (max-width: 768px) {
  .login-form {
    min-width: 300px;
  }

  .form-row {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }
}
</style>
