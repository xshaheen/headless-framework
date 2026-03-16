<script lang="ts">
export class ConfirmDialogProps {
  icon?: string = 'mdi-alert-circle'
  iconColor?: string = '#F44336'
  cancelText?: string = 'Cancel'
  confirmText?: string = 'Confirm'
  cancelColor?: string = '#9E9E9E'
  confirmColor?: string = '#F44336'
  title?: string = 'Confirm Action'
  text?: string = 'Are you sure you want to proceed?'
  maxWidth?: string = '500'
  showCancel?: boolean = true
  showConfirm?: boolean = true
}
</script>

<script setup lang="ts">
import { toRef, type PropType } from 'vue'

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'confirm'): void
}>()

const props = defineProps({
  dialogProps: {
    type: Object as PropType<ConfirmDialogProps>,
    default: () => new ConfirmDialogProps(),
  },
  isOpen: {
    type: Boolean,
    required: true,
  },
})
</script>

<template>
  <div class="text-center pa-4">
    <v-dialog v-model="toRef(props, 'isOpen').value" :max-width="dialogProps.maxWidth" persistent>
      <v-card :title="dialogProps.title">
        <template #text>
          <span>{{ dialogProps.text }}</span>
        </template>

        <template #prepend>
          <v-icon :color="dialogProps.iconColor">{{ dialogProps.icon }}</v-icon>
        </template>

        <template #actions>
          <v-spacer />
          <v-btn v-if="dialogProps.showCancel" :color="dialogProps.cancelColor" @click="emit('close')">
            {{ dialogProps.cancelText }}
          </v-btn>
          <v-btn v-if="dialogProps.showConfirm" :color="dialogProps.confirmColor" @click="emit('confirm')">
            {{ dialogProps.confirmText }}
          </v-btn>
        </template>
      </v-card>
    </v-dialog>
  </div>
</template>
