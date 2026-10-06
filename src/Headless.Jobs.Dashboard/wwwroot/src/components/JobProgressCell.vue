<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { formatProgressAge, formatProgressPercent, jobProgressDisplay } from '@/utilities/job-progress'

const props = defineProps<{
  status: string | number
  percent?: number | null
  message?: string | null
  updatedAt?: string | null
}>()

const display = computed(() => jobProgressDisplay(props.status, props.percent))
const percentValue = computed(() => Math.min(100, Math.max(0, props.percent ?? 0)))
const percentText = computed(() => formatProgressPercent(percentValue.value))

// The age is read when the tooltip opens rather than on a timer, so a table of running jobs does not re-render every
// second just to keep hidden tooltips current.
const tooltipOpen = ref(false)
const now = ref(Date.now())
watch(tooltipOpen, (open) => {
  if (open) now.value = Date.now()
})
const ageText = computed(() => formatProgressAge(props.updatedAt, now.value))
</script>

<template>
  <div
    v-if="display !== 'none'"
    class="job-progress"
    :class="{ 'job-progress--muted': display === 'muted' }"
    data-testid="job-progress"
  >
    <v-progress-linear
      :model-value="percentValue"
      :color="display === 'muted' ? 'grey' : '#6495ED'"
      bg-color="grey"
      height="4"
      rounded
      :aria-label="`Job progress ${percentText}`"
    />
    <div class="job-progress__label text-caption">
      <span class="job-progress__percent">{{ percentText }}</span>
      <span v-if="message" class="job-progress__message">· {{ message }}</span>
    </div>
    <v-tooltip v-model="tooltipOpen" activator="parent" location="top" max-width="400">
      <div v-if="message" class="job-progress__tooltip-message">{{ message }}</div>
      <div>{{ percentText }}<template v-if="ageText"> · {{ ageText }}</template></div>
    </v-tooltip>
  </div>
</template>

<style scoped>
.job-progress {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 120px;
  max-width: 220px;
  padding-block: 4px;
}

.job-progress__label {
  display: flex;
  gap: 4px;
  min-width: 0;
  line-height: 1.2;
}

.job-progress__percent {
  flex-shrink: 0;
  font-weight: 500;
}

.job-progress__message {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-progress--muted .job-progress__label {
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
}

.job-progress__tooltip-message {
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
