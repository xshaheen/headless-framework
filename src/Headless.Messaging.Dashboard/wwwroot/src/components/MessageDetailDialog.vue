<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  modelValue: boolean
  messageContent: string
}

const props = defineProps<Props>()
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const isOpen = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const formattedContent = computed(() => {
  if (!props.messageContent) return ''
  try {
    const parsed = JSON.parse(props.messageContent)
    return JSON.stringify(parsed, null, 2)
  } catch {
    return props.messageContent
  }
})
</script>

<template>
  <v-dialog v-model="isOpen" max-width="700" scrollable>
    <v-card title="Message Detail">
      <template #prepend>
        <v-icon color="primary">mdi-code-json</v-icon>
      </template>

      <v-card-text>
        <pre class="message-content">{{ formattedContent }}</pre>
      </v-card-text>

      <v-card-actions>
        <v-spacer />
        <v-btn color="primary" variant="text" @click="isOpen = false"> Close </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.message-content {
  white-space: pre-wrap;
  word-wrap: break-word;
  background: rgba(0, 0, 0, 0.2);
  padding: 16px;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.85rem;
  line-height: 1.5;
  max-height: 60vh;
  overflow-y: auto;
  color: #e0e0e0;
}
</style>
