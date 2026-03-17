<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import JSONBig from 'json-bigint'
import { formatDateTime, timeAgo } from '@/utilities/dateTimeParser'

export interface MessageDetail {
  id: string
  name: string
  content: string
  added: string
  expiresAt: string | null
  retries: number
  group?: string
  exceptionInfo?: string
}

interface Props {
  modelValue: boolean
  message: MessageDetail | null
}

// --- Exception types (mirrors ConfirmDialog pattern) ---
interface StructuredException {
  type: 'structured'
  message: string
  details: unknown
  stackTrace?: string
  source?: string
  helpLink?: string
  data?: unknown
  innerException?: unknown
}

interface GenericException {
  type: 'generic'
  details: unknown
}

interface PlainText {
  type: 'plain'
  text: string
}

type ContentKind = StructuredException | GenericException | PlainText

const JSONBigParser = JSONBig({ storeAsString: true })

function parseContent(raw: string): { parsed: unknown; isJson: boolean } {
  try {
    return { parsed: JSONBigParser.parse(raw), isJson: true }
  } catch {
    return { parsed: null, isJson: false }
  }
}

function isExceptionObject(obj: unknown): boolean {
  if (!obj || typeof obj !== 'object') return false
  const o = obj as Record<string, unknown>
  return (
    ('Message' in o || 'message' in o) &&
    ('StackTrace' in o || 'stackTrace' in o || 'StackTraceString' in o || 'stack' in o)
  )
}

function buildContentKind(raw: string): ContentKind {
  const { parsed, isJson } = parseContent(raw)

  if (!isJson) return { type: 'plain', text: raw }

  if (typeof parsed === 'string') return { type: 'plain', text: parsed }

  if (isExceptionObject(parsed)) {
    const o = parsed as Record<string, unknown>
    if (o.Message) {
      return {
        type: 'structured',
        message: String(o.Message),
        details: parsed,
        stackTrace: (o.StackTrace ?? o.StackTraceString) as string | undefined,
        source: o.Source as string | undefined,
        helpLink: o.HelpLink as string | undefined,
        data: o.Data,
        innerException: o.InnerException,
      }
    }
    return {
      type: 'structured',
      message: String(o.message),
      details: parsed,
      stackTrace: (o.stack ?? o.stackTrace) as string | undefined,
      source: o.source as string | undefined,
      helpLink: o.helpLink as string | undefined,
      data: o.data,
      innerException: o.innerException,
    }
  }

  return { type: 'generic', details: parsed }
}

const props = defineProps<Props>()
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const isOpen = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const showStackTrace = ref(false)
const showRawData = ref(false)
const copied = ref(false)
const activeTab = ref<'content' | 'exception'>('content')

const hasExceptionInfo = computed(() => !!props.message?.exceptionInfo?.trim())

watch(
  () => props.message,
  (msg) => {
    activeTab.value = msg?.exceptionInfo?.trim() ? 'exception' : 'content'
  },
  { immediate: true },
)

const contentKind = computed((): ContentKind | null => {
  if (!props.message?.content) return null
  return buildContentKind(props.message.content)
})

const isException = computed(() => contentKind.value?.type === 'structured')
const isPlain = computed(() => contentKind.value?.type === 'plain')

function highlightJson(jsonStr: string): string {
  const html = jsonStr
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
  return html.replace(
    /("(\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+-]?\d+)?)/g,
    (match) => {
      let cls = 'json-number'
      if (match.startsWith('"')) {
        cls = match.endsWith(':') ? 'json-key' : 'json-string'
      } else if (/true|false/.test(match)) {
        cls = 'json-boolean'
      } else if (match === 'null') {
        cls = 'json-null'
      }
      return `<span class="${cls}">${match}</span>`
    },
  )
}

const formattedJsonHtml = computed(() => {
  if (!props.message?.content) return ''
  const { parsed, isJson } = parseContent(props.message.content)
  if (!isJson) return props.message.content
  try {
    return highlightJson(JSON.stringify(parsed, null, 2))
  } catch {
    return props.message.content
  }
})

const structured = computed((): StructuredException | null => {
  if (contentKind.value?.type !== 'structured') return null
  return contentKind.value as StructuredException
})

const hasStackTrace = computed(() => {
  const st = structured.value?.stackTrace
  return !!st && st.trim().length > 0
})

const plainText = computed(() => {
  if (contentKind.value?.type !== 'plain') return ''
  return (contentKind.value as PlainText).text
})

const hasAdditionalInfo = computed(() => {
  const s = structured.value
  return !!(s?.source || s?.helpLink || s?.data || s?.innerException)
})

async function copyContent() {
  if (!props.message?.content) return
  try {
    await navigator.clipboard.writeText(props.message.content)
    copied.value = true
    setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    // clipboard not available
  }
}
</script>

<template>
  <v-dialog v-model="isOpen" max-width="780" scrollable>
    <v-card class="detail-dialog">
      <!-- Toolbar -->
      <v-toolbar density="compact" color="transparent" class="detail-toolbar px-2">
        <v-icon color="primary" class="mr-2">mdi-code-json</v-icon>
        <v-toolbar-title class="text-body-1 font-weight-bold">Message Detail</v-toolbar-title>
        <v-spacer />
        <!-- Copy button -->
        <v-tooltip :text="copied ? 'Copied!' : 'Copy content'" location="bottom">
          <template #activator="{ props: tooltipProps }">
            <v-btn
              v-bind="tooltipProps"
              :icon="copied ? 'mdi-check' : 'mdi-content-copy'"
              :color="copied ? 'success' : 'default'"
              size="small"
              variant="text"
              @click="copyContent"
            />
          </template>
        </v-tooltip>
        <v-btn icon="mdi-close" size="small" variant="text" @click="isOpen = false" />
      </v-toolbar>

      <v-divider />

      <!-- Metadata header -->
      <div v-if="message" class="metadata-section px-4 py-3">
        <div class="metadata-chips">
          <!-- Name -->
          <v-chip size="small" color="primary" variant="tonal" class="mr-1 mb-1">
            <v-icon start size="x-small">mdi-tag</v-icon>
            {{ message.name }}
          </v-chip>

          <!-- Group (received only) -->
          <v-chip v-if="message.group" size="small" color="secondary" variant="tonal" class="mr-1 mb-1">
            <v-icon start size="x-small">mdi-group</v-icon>
            {{ message.group }}
          </v-chip>

          <!-- Exception indicator -->
          <v-chip v-if="isException" size="small" color="error" variant="tonal" class="mr-1 mb-1">
            <v-icon start size="x-small">mdi-alert-circle</v-icon>
            Exception
          </v-chip>
        </div>

        <div class="metadata-fields mt-2">
          <div class="meta-item">
            <span class="meta-label">Added</span>
            <v-tooltip :text="timeAgo(message.added)" location="top">
              <template #activator="{ props: tp }">
                <span v-bind="tp" class="meta-value">{{ formatDateTime(message.added) }}</span>
              </template>
            </v-tooltip>
          </div>
          <div v-if="message.expiresAt" class="meta-item">
            <span class="meta-label">Expires At</span>
            <v-tooltip :text="timeAgo(message.expiresAt)" location="top">
              <template #activator="{ props: tp }">
                <span v-bind="tp" class="meta-value">{{ formatDateTime(message.expiresAt) }}</span>
              </template>
            </v-tooltip>
          </div>
          <div class="meta-item">
            <span class="meta-label">Retries</span>
            <span class="meta-value">{{ message.retries }}</span>
          </div>
        </div>
      </div>

      <v-divider />

      <!-- Tab bar — only shown when there's exception info -->
      <v-tabs v-if="hasExceptionInfo" v-model="activeTab" density="compact" class="content-tabs">
        <v-tab value="content">
          <v-icon start size="small">mdi-code-json</v-icon>
          Content
        </v-tab>
        <v-tab value="exception">
          <v-icon start size="small" color="error">mdi-alert-circle-outline</v-icon>
          Exception
        </v-tab>
      </v-tabs>
      <v-divider v-if="hasExceptionInfo" />

      <v-card-text class="content-area">
        <!-- Exception tab -->
        <div v-if="hasExceptionInfo && activeTab === 'exception'" class="exception-info-content">
          <pre class="stack-trace-code exception-info-pre">{{ message?.exceptionInfo }}</pre>
        </div>

        <!-- Content tab (or always shown when no exception) -->
        <template v-else>
          <!-- Exception rendering -->
          <div v-if="isException && structured" class="exception-content">
            <!-- Main Exception Message -->
            <div class="exception-message mb-4">
              <v-icon size="small" color="error" class="mr-2">mdi-alert-circle</v-icon>
              <span class="exception-text">{{ structured.message }}</span>
            </div>

            <!-- Stack Trace -->
            <div v-if="hasStackTrace" class="stack-trace-section mb-4">
              <div class="section-header stack-trace-header" @click="showStackTrace = !showStackTrace">
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
                <pre class="stack-trace-code">{{ structured.stackTrace }}</pre>
              </div>
            </div>

            <!-- Additional Info -->
            <div v-if="hasAdditionalInfo" class="additional-info mb-4">
              <div class="section-header info-header">
                <v-icon size="small" class="mr-2" color="info">mdi-information</v-icon>
                <span class="section-title">Additional Information</span>
              </div>
              <div class="info-grid">
                <div v-if="structured.source" class="info-item">
                  <span class="info-label">Source</span>
                  <span class="info-value">{{ structured.source }}</span>
                </div>
                <div v-if="structured.helpLink" class="info-item">
                  <span class="info-label">Help Link</span>
                  <a :href="structured.helpLink" target="_blank" class="info-link">
                    {{ structured.helpLink }}
                  </a>
                </div>
                <div v-if="structured.data" class="info-item">
                  <span class="info-label">Data</span>
                  <pre class="info-data">{{ JSON.stringify(structured.data, null, 2) }}</pre>
                </div>
                <div v-if="structured.innerException" class="info-item">
                  <span class="info-label">Inner Exception</span>
                  <pre class="info-data">{{ JSON.stringify(structured.innerException, null, 2) }}</pre>
                </div>
              </div>
            </div>

            <!-- Raw Exception Data -->
            <div class="raw-data-section">
              <div class="section-header raw-data-header" @click="showRawData = !showRawData">
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
                <pre class="raw-data-code">{{ JSON.stringify(structured.details, null, 2) }}</pre>
              </div>
            </div>
          </div>

          <!-- Plain text -->
          <div v-else-if="isPlain" class="plain-text-wrapper">
            <div class="plain-indicator mb-2">
              <v-icon size="x-small" class="mr-1" color="secondary">mdi-text</v-icon>
              <span class="text-caption text-medium-emphasis">Text content</span>
            </div>
            <pre class="message-content">{{ plainText }}</pre>
          </div>

          <!-- JSON / Generic -->
          <pre v-else class="message-content" v-html="formattedJsonHtml"></pre>
        </template>
      </v-card-text>

      <v-card-actions>
        <v-spacer />
        <v-btn color="primary" variant="text" @click="isOpen = false">Close</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.detail-dialog {
  max-height: 90vh;
  display: flex;
  flex-direction: column;
}

.detail-toolbar {
  flex-shrink: 0;
}

.metadata-section {
  background: rgba(255, 255, 255, 0.02);
  flex-shrink: 0;
}

.metadata-chips {
  display: flex;
  flex-wrap: wrap;
}

.metadata-fields {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.meta-label {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #9e9e9e;
}

.meta-value {
  font-size: 0.8rem;
  color: #e0e0e0;
}

.content-area {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
}

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
  color: #e0e0e0;
  margin: 0;
}

.plain-text-wrapper {
  .plain-indicator {
    display: flex;
    align-items: center;
  }
}

/* Exception styles (mirrors ConfirmDialog) */
.exception-content {
  text-align: left;
}

.exception-message {
  display: flex;
  align-items: flex-start;
  background: rgba(244, 67, 54, 0.1);
  border: 1px solid rgba(244, 67, 54, 0.2);
  border-radius: 8px;
  padding: 16px;
  font-weight: 600;
  color: #ef9a9a;
}

.exception-text {
  font-size: 1rem;
  line-height: 1.4;
  word-break: break-word;
}

.section-header {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  margin-bottom: 8px;
  cursor: pointer;
  transition: background 0.2s ease;
  font-weight: 600;
}

.section-header:hover {
  background: rgba(255, 255, 255, 0.08);
}

.stack-trace-header {
  border-left: 4px solid #1976d2;
}

.info-header {
  border-left: 4px solid #0288d1;
  cursor: default;
}

.raw-data-header {
  border-left: 4px solid #f57c00;
}

.section-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: #e0e0e0;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.toggle-icon {
  transition: transform 0.25s ease;
  color: #9e9e9e;
}

.toggle-icon.rotated {
  transform: rotate(180deg);
}

.stack-trace-content,
.raw-data-content {
  background: rgba(0, 0, 0, 0.25);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 8px;
  max-height: 250px;
  overflow-y: auto;
}

.stack-trace-content {
  border-left: 3px solid rgba(25, 118, 210, 0.4);
}

.raw-data-content {
  border-left: 3px solid rgba(245, 124, 0, 0.4);
}

.stack-trace-code,
.raw-data-code {
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.78rem;
  line-height: 1.4;
  margin: 0;
  white-space: pre-wrap;
  word-wrap: break-word;
  color: #e0e0e0;
}

.additional-info .info-header {
  margin-bottom: 8px;
}

.info-grid {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 0 4px;
}

.info-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 12px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.info-label {
  font-weight: 700;
  color: #90caf9;
  min-width: 90px;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.info-value {
  color: #e0e0e0;
  font-size: 0.82rem;
  word-break: break-word;
  flex: 1;
}

.info-link {
  color: #64b5f6;
  text-decoration: none;
  font-size: 0.82rem;
  word-break: break-all;
}

.info-link:hover {
  text-decoration: underline;
}

.info-data {
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.75rem;
  background: rgba(0, 0, 0, 0.15);
  padding: 6px 8px;
  border-radius: 4px;
  margin: 0;
  white-space: pre-wrap;
  word-wrap: break-word;
  max-height: 120px;
  overflow-y: auto;
  border: 1px solid rgba(255, 255, 255, 0.06);
  color: #e0e0e0;
  flex: 1;
}

.content-tabs {
  flex-shrink: 0;
}

.exception-info-content {
  height: 100%;
}

.exception-info-pre {
  font-family: 'JetBrains Mono', 'Monaco', 'Consolas', monospace;
  font-size: 0.78rem;
  line-height: 1.5;
  margin: 0;
  white-space: pre-wrap;
  word-wrap: break-word;
  color: #e0e0e0;
}

/* JSON syntax highlighting */
.message-content :deep(.json-key) {
  color: #9cdcfe;
}

.message-content :deep(.json-string) {
  color: #ce9178;
}

.message-content :deep(.json-number) {
  color: #b5cea8;
}

.message-content :deep(.json-boolean) {
  color: #569cd6;
}

.message-content :deep(.json-null) {
  color: #569cd6;
  font-style: italic;
}
</style>
