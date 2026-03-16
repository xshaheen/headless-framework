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
  isCode?: boolean = false
  isException?: boolean = false
  showCancel?: boolean = true
  showConfirm?: boolean = true
  showWarningAlert?: boolean = false
  warningAlertMessage?: string = ''
}

// Type definitions for exception data
interface StructuredException {
  type: 'structured'
  message: string
  details: any
  stackTrace?: string
  source?: string
  helpLink?: string
  data?: any
  innerException?: any
}

interface GenericException {
  type: 'generic'
  details: any
}

interface PlainException {
  type: 'plain'
  text: string
}

type ParsedContent = StructuredException | GenericException | PlainException
</script>

<script setup lang="ts">
import { ref, computed, toRef, type PropType } from 'vue'

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

// Collapsible section state
const showStackTrace = ref(false)
const showRawData = ref(false)

const toggleStackTrace = () => {
  showStackTrace.value = !showStackTrace.value
}

const toggleRawData = () => {
  showRawData.value = !showRawData.value
}

// Exception parsing
function parseExceptionContent(content: string): ParsedContent {
  try {
    const parsed = JSON.parse(content)

    if (typeof parsed === 'string') {
      return { type: 'plain', text: parsed }
    }

    if (parsed.Message) {
      return {
        type: 'structured',
        message: parsed.Message,
        details: parsed,
        stackTrace: parsed.StackTrace || parsed.StackTraceString,
        source: parsed.Source,
        helpLink: parsed.HelpLink,
        data: parsed.Data,
        innerException: parsed.InnerException,
      }
    }

    if (parsed.message) {
      return {
        type: 'structured',
        message: parsed.message,
        details: parsed,
        stackTrace: parsed.stack || parsed.stackTrace,
        source: parsed.source,
        helpLink: parsed.helpLink,
        data: parsed.data,
        innerException: parsed.innerException,
      }
    }

    return { type: 'generic', details: parsed }
  } catch {
    return { type: 'plain', text: content }
  }
}

const parsedContent = computed((): ParsedContent | null => {
  if (!props.dialogProps.isException || !props.dialogProps.text) return null
  return parseExceptionContent(props.dialogProps.text)
})

const isStructuredException = computed(
  () => parsedContent.value?.type === 'structured',
)

const hasStackTrace = computed(() => {
  if (!isStructuredException.value) return false
  const ex = parsedContent.value as StructuredException
  return !!ex.stackTrace && ex.stackTrace.trim().length > 0
})

const hasAdditionalInfo = computed(() => {
  if (!isStructuredException.value) return false
  const ex = parsedContent.value as StructuredException
  return !!(ex.source || ex.helpLink || ex.data || ex.innerException)
})

const exceptionMessage = computed(() => {
  if (!isStructuredException.value) return 'An error occurred'
  return (parsedContent.value as StructuredException).message || 'An error occurred'
})

const exceptionStackTrace = computed(() => {
  if (!isStructuredException.value) return ''
  return (parsedContent.value as StructuredException).stackTrace || ''
})

const exceptionDetails = computed(() => {
  if (!isStructuredException.value) return {}
  return (parsedContent.value as StructuredException).details || {}
})

const exceptionSource = computed(() => {
  if (!isStructuredException.value) return ''
  return (parsedContent.value as StructuredException).source || ''
})

const exceptionHelpLink = computed(() => {
  if (!isStructuredException.value) return ''
  return (parsedContent.value as StructuredException).helpLink || ''
})

const exceptionData = computed(() => {
  if (!isStructuredException.value) return null
  return (parsedContent.value as StructuredException).data || null
})
</script>

<template>
  <div class="text-center pa-4">
    <v-dialog v-model="toRef(props, 'isOpen').value" :max-width="dialogProps.maxWidth" persistent>
      <v-card :title="dialogProps.title" class="exception-dialog">
        <template #text>
          <!-- Warning Alert -->
          <v-alert
            v-if="dialogProps.showWarningAlert"
            class="mb-4"
            icon="mdi-alert-circle-outline"
            color="warning"
            :text="dialogProps.warningAlertMessage"
          />

          <!-- Exception rendering -->
          <div v-if="dialogProps.isException && isStructuredException" class="exception-content">
            <!-- Main Exception Message -->
            <div class="exception-message">
              <v-icon size="small" color="error" class="mr-2">mdi-alert-circle</v-icon>
              <span class="exception-text">{{ exceptionMessage }}</span>
            </div>

            <!-- Stack Trace Section -->
            <div v-if="hasStackTrace" class="stack-trace-section">
              <div class="section-header stack-trace-header" @click="toggleStackTrace">
                <v-icon size="small" class="mr-2" color="primary">mdi-code-braces</v-icon>
                <span class="section-title">Stack Trace</span>
                <v-chip size="x-small" color="primary" variant="tonal" class="ml-2">
                  {{ showStackTrace ? 'Collapse' : 'Expand' }}
                </v-chip>
                <v-icon
                  size="small"
                  class="ml-auto toggle-icon"
                  :class="{ rotated: showStackTrace }"
                >
                  mdi-chevron-down
                </v-icon>
              </div>
              <div v-show="showStackTrace" class="stack-trace-content">
                <pre class="stack-trace-code">{{ exceptionStackTrace }}</pre>
              </div>
            </div>

            <!-- Additional Information -->
            <div v-if="hasAdditionalInfo" class="additional-info">
              <div class="section-header info-header">
                <v-icon size="small" class="mr-2" color="info">mdi-information</v-icon>
                <span class="section-title">Additional Information</span>
              </div>

              <div class="info-grid">
                <div v-if="exceptionSource" class="info-item">
                  <span class="info-label">Source:</span>
                  <span class="info-value">{{ exceptionSource }}</span>
                </div>

                <div v-if="exceptionHelpLink" class="info-item">
                  <span class="info-label">Help Link:</span>
                  <a :href="exceptionHelpLink" target="_blank" class="info-link">
                    {{ exceptionHelpLink }}
                  </a>
                </div>

                <div v-if="exceptionData" class="info-item">
                  <span class="info-label">Data:</span>
                  <pre class="info-data">{{ JSON.stringify(exceptionData, null, 2) }}</pre>
                </div>
              </div>
            </div>

            <!-- Raw Exception Data (Collapsible) -->
            <div class="raw-data-section">
              <div class="section-header raw-data-header" @click="toggleRawData">
                <v-icon size="small" class="mr-2" color="warning">mdi-json</v-icon>
                <span class="section-title">Raw Exception Data</span>
                <v-chip size="x-small" color="warning" variant="tonal" class="ml-2">
                  {{ showRawData ? 'Collapse' : 'Expand' }}
                </v-chip>
                <v-icon
                  size="small"
                  class="ml-auto toggle-icon"
                  :class="{ rotated: showRawData }"
                >
                  mdi-chevron-down
                </v-icon>
              </div>
              <div v-show="showRawData" class="raw-data-content">
                <pre class="raw-data-code">{{ JSON.stringify(exceptionDetails, null, 2) }}</pre>
              </div>
            </div>
          </div>

          <!-- Code / monospace display -->
          <pre v-else-if="dialogProps.isCode" class="json-box">{{ dialogProps.text }}</pre>

          <!-- Regular text display -->
          <span v-else>{{ dialogProps.text }}</span>
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

<style scoped>
.json-box {
  white-space: pre-wrap;
  word-wrap: break-word;
  background: rgba(0, 0, 0, 0.05);
  padding: 12px;
  border-radius: 6px;
  border: 1px solid rgba(0, 0, 0, 0.1);
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.875rem;
  line-height: 1.4;
  max-height: 400px;
  overflow-y: auto;
}

/* Exception Dialog Styles */
.exception-dialog {
  max-height: 80vh;
  overflow: hidden;
}

.exception-content {
  text-align: left;
  max-height: 60vh;
  overflow-y: auto;
}

.exception-message {
  display: flex;
  align-items: flex-start;
  background: rgba(244, 67, 54, 0.1);
  border: 1px solid rgba(244, 67, 54, 0.2);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
  font-weight: 600;
  color: #d32f2f;
}

.exception-text {
  font-size: 1rem;
  line-height: 1.4;
  word-break: break-word;
}

.section-header {
  display: flex;
  align-items: center;
  padding: 16px 20px;
  background: rgba(0, 0, 0, 0.04);
  border: 2px solid rgba(0, 0, 0, 0.08);
  border-radius: 8px;
  margin-bottom: 12px;
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  font-weight: 600;
  color: #424242;
  position: relative;
  overflow: hidden;
}

.section-header::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 3px;
  background: linear-gradient(90deg, transparent, rgba(100, 181, 246, 0.3), transparent);
  transition: all 0.3s ease;
}

.section-header:hover {
  background: rgba(0, 0, 0, 0.08);
  border-color: rgba(100, 181, 246, 0.3);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.section-header:hover::before {
  background: linear-gradient(90deg, transparent, rgba(100, 181, 246, 0.6), transparent);
}

/* Specific header styles for different sections */
.stack-trace-header {
  border-left: 4px solid #1976d2;
  background: linear-gradient(135deg, rgba(25, 118, 210, 0.05) 0%, rgba(25, 118, 210, 0.02) 100%);
}

.stack-trace-header::before {
  background: linear-gradient(90deg, transparent, rgba(25, 118, 210, 0.4), transparent);
}

.stack-trace-header:hover {
  border-color: rgba(25, 118, 210, 0.5);
  background: linear-gradient(135deg, rgba(25, 118, 210, 0.08) 0%, rgba(25, 118, 210, 0.04) 100%);
  box-shadow: 0 4px 16px rgba(25, 118, 210, 0.2);
}

.info-header {
  border-left: 4px solid #0288d1;
  background: linear-gradient(135deg, rgba(2, 136, 209, 0.05) 0%, rgba(2, 136, 209, 0.02) 100%);
}

.info-header::before {
  background: linear-gradient(90deg, transparent, rgba(2, 136, 209, 0.4), transparent);
}

.info-header:hover {
  border-color: rgba(2, 136, 209, 0.5);
  background: linear-gradient(135deg, rgba(2, 136, 209, 0.08) 0%, rgba(2, 136, 209, 0.04) 100%);
  box-shadow: 0 4px 16px rgba(2, 136, 209, 0.2);
}

.raw-data-header {
  border-left: 4px solid #f57c00;
  background: linear-gradient(135deg, rgba(245, 124, 0, 0.05) 0%, rgba(245, 124, 0, 0.02) 100%);
}

.raw-data-header::before {
  background: linear-gradient(90deg, transparent, rgba(245, 124, 0, 0.4), transparent);
}

.raw-data-header:hover {
  border-color: rgba(245, 124, 0, 0.5);
  background: linear-gradient(135deg, rgba(245, 124, 0, 0.08) 0%, rgba(245, 124, 0, 0.04) 100%);
  box-shadow: 0 4px 16px rgba(245, 124, 0, 0.2);
}

.section-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: #424242;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.toggle-icon {
  transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  color: #666;
}

.toggle-icon.rotated {
  transform: rotate(180deg);
  color: #1976d2;
}

.stack-trace-section,
.raw-data-section {
  position: relative;
  margin-bottom: 20px;
}

.stack-trace-section::after,
.raw-data-section::after {
  content: '';
  position: absolute;
  bottom: -10px;
  left: 0;
  right: 0;
  height: 1px;
  background: linear-gradient(90deg, transparent, rgba(0, 0, 0, 0.1), transparent);
}

.stack-trace-content,
.raw-data-content {
  background: rgba(0, 0, 0, 0.03);
  border: 2px solid rgba(0, 0, 0, 0.08);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
  max-height: 250px;
  overflow-y: auto;
  position: relative;
  transition: all 0.3s ease;
  animation: slideDown 0.3s ease-out;
}

@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.stack-trace-content::before,
.raw-data-content::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 2px;
  background: linear-gradient(90deg, rgba(100, 181, 246, 0.3), rgba(100, 181, 246, 0.1));
  border-radius: 8px 8px 0 0;
}

.stack-trace-content {
  border-left: 4px solid rgba(25, 118, 210, 0.3);
}

.raw-data-content {
  border-left: 4px solid rgba(245, 124, 0, 0.3);
}

.stack-trace-code,
.raw-data-code {
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.8rem;
  line-height: 1.4;
  margin: 0;
  white-space: pre-wrap;
  word-wrap: break-word;
  color: #2c3e50;
  background: rgba(255, 255, 255, 0.5);
  padding: 12px;
  border-radius: 6px;
  border: 1px solid rgba(0, 0, 0, 0.05);
  box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.05);
}

.additional-info {
  margin-bottom: 16px;
}

.info-grid {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 0 20px;
}

.info-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.6);
  border-radius: 8px;
  border: 1px solid rgba(0, 0, 0, 0.06);
  transition: all 0.2s ease;
  position: relative;
  overflow: hidden;
}

.info-item::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 3px;
  background: linear-gradient(to bottom, rgba(2, 136, 209, 0.6), rgba(2, 136, 209, 0.3));
}

.info-item:hover {
  background: rgba(255, 255, 255, 0.8);
  border-color: rgba(2, 136, 209, 0.2);
  transform: translateX(2px);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.info-label {
  font-weight: 700;
  color: #1976d2;
  min-width: 90px;
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  background: rgba(25, 118, 210, 0.1);
  padding: 4px 8px;
  border-radius: 4px;
  text-align: center;
}

.info-value {
  color: #2c3e50;
  font-size: 0.85rem;
  word-break: break-word;
  font-weight: 500;
  flex: 1;
}

.info-link {
  color: #1976d2;
  text-decoration: none;
  font-size: 0.85rem;
  word-break: break-all;
  font-weight: 500;
  transition: all 0.2s ease;
}

.info-link:hover {
  text-decoration: underline;
  color: #1565c0;
  text-shadow: 0 0 8px rgba(25, 118, 210, 0.3);
}

.info-data {
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.75rem;
  background: rgba(0, 0, 0, 0.04);
  padding: 8px 10px;
  border-radius: 6px;
  margin: 0;
  white-space: pre-wrap;
  word-wrap: break-word;
  max-height: 120px;
  overflow-y: auto;
  border: 1px solid rgba(0, 0, 0, 0.08);
  color: #2c3e50;
  font-weight: 500;
}

.raw-data-section {
  margin-top: 16px;
}

/* Responsive adjustments */
@media (max-width: 768px) {
  .exception-content {
    max-height: 50vh;
  }

  .stack-trace-content,
  .raw-data-content {
    max-height: 150px;
  }

  .info-item {
    flex-direction: column;
    gap: 4px;
  }

  .info-label {
    min-width: auto;
  }
}
</style>
