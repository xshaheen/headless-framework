<template>
  <div class="app-container">
    <div v-if="showDashboardLayout" class="dashboard-wrapper">
      <DashboardLayout>
        <template #default>
          <RouterView />
        </template>
      </DashboardLayout>
      <GlobalAlerts />
    </div>

    <div v-else class="standalone-wrapper">
      <RouterView />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRoute } from 'vue-router'
import DashboardLayout from './components/layout/DashboardLayout.vue'

const GlobalAlerts = defineAsyncComponent(() => import('./components/common/GlobalAlerts.vue'))

const route = useRoute()

const showDashboardLayout = computed(() => {
  return route.name !== 'Login'
})
</script>

<style scoped>
.app-container {
  min-height: 100vh;
}

.dashboard-wrapper,
.standalone-wrapper {
  min-height: 100vh;
}

.standalone-wrapper {
  display: flex;
  flex-direction: column;
}
</style>
