<template>
  <div class="messaging-charts">
    <!-- Real-time Metrics Line Chart -->
    <v-card class="chart-card mb-6">
      <v-card-title class="chart-title">
        <v-icon class="mr-2">mdi-chart-line</v-icon>
        Real-time Metrics
      </v-card-title>
      <v-card-text class="chart-body">
        <VChart :option="realtimeChartOption" class="chart" autoresize />
      </v-card-text>
    </v-card>

    <!-- Hourly History Area Chart -->
    <v-card class="chart-card mb-6">
      <v-card-title class="chart-title">
        <v-icon class="mr-2">mdi-chart-area-spline</v-icon>
        Hourly History (24h)
      </v-card-title>
      <v-card-text class="chart-body">
        <VChart :option="historyChartOption" class="chart" autoresize />
      </v-card-text>
    </v-card>

    <!-- Success/Failure Pie Chart -->
    <v-card class="chart-card">
      <v-card-title class="chart-title">
        <v-icon class="mr-2">mdi-chart-pie</v-icon>
        Message Distribution
      </v-card-title>
      <v-card-text class="chart-body chart-body--pie">
        <VChart :option="pieChartOption" class="chart" autoresize />
      </v-card-text>
    </v-card>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, PieChart, ScatterChart } from 'echarts/charts'
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  TitleComponent,
} from 'echarts/components'
import { storeToRefs } from 'pinia'
import { useMessagingStore } from '@/stores/messagingStore'

use([
  CanvasRenderer,
  LineChart,
  PieChart,
  ScatterChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  TitleComponent,
])

// --- Dark theme palette ---
const TEXT_COLOR = '#e0e0e0'
const GRID_LINE_COLOR = 'rgba(255,255,255,0.05)'
const TOOLTIP_BG = 'rgba(42,42,42,0.95)'
const TOOLTIP_BORDER = 'rgba(255,255,255,0.1)'

const COLOR_GREEN = '#4caf50'
const COLOR_RED = '#f44336'
const COLOR_BLUE = '#2196f3'
const COLOR_AMBER = '#ff9800'

const store = useMessagingStore()
const { stats, realtimeMetrics, metricsHistory } = storeToRefs(store)

// --- Helpers ---

function formatTimestamp(ts: number | null): string {
  if (ts == null) return ''
  const d = new Date(ts * 1000)
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  const ss = String(d.getSeconds()).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
}

// --- Chart 1: Real-time line chart (dual Y-axis) ---

const realtimeChartOption = computed(() => {
  const raw = realtimeMetrics.value
  const timestamps: string[] = []
  const publishedPerSec: (number | null)[] = []
  const subscribedPerSec: (number | null)[] = []
  const latencyMs: (number | null)[] = []

  if (raw && raw.length >= 4) {
    const tsArr = raw[0]
    const pubArr = raw[1]
    const subArr = raw[2]
    const latArr = raw[3]
    const len = tsArr?.length ?? 0

    for (let i = 0; i < len; i++) {
      timestamps.push(formatTimestamp(tsArr[i]))
      publishedPerSec.push(pubArr?.[i] ?? null)
      subscribedPerSec.push(subArr?.[i] ?? null)
      latencyMs.push(latArr?.[i] ?? null)
    }
  }

  return {
    backgroundColor: 'transparent',
    tooltip: {
      trigger: 'axis',
      backgroundColor: TOOLTIP_BG,
      borderColor: TOOLTIP_BORDER,
      borderWidth: 1,
      textStyle: { color: TEXT_COLOR, fontSize: 12 },
      axisPointer: { type: 'cross', crossStyle: { color: 'rgba(255,255,255,0.3)' } },
    },
    legend: {
      data: ['Published/s', 'Subscribed/s', 'Latency (ms)'],
      textStyle: { color: TEXT_COLOR },
      top: 8,
    },
    grid: {
      left: 56,
      right: 64,
      top: 48,
      bottom: 40,
      containLabel: false,
    },
    xAxis: {
      type: 'category',
      data: timestamps,
      axisLabel: { color: TEXT_COLOR, fontSize: 11 },
      axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
      splitLine: { show: false },
    },
    yAxis: [
      {
        type: 'value',
        name: 'Rate (TPS)',
        nameTextStyle: { color: TEXT_COLOR, fontSize: 11 },
        axisLabel: { color: TEXT_COLOR, fontSize: 11 },
        axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
        splitLine: { lineStyle: { color: GRID_LINE_COLOR, type: 'dashed' } },
        min: 0,
      },
      {
        type: 'value',
        name: 'Elapsed (ms)',
        nameTextStyle: { color: TEXT_COLOR, fontSize: 11 },
        axisLabel: { color: TEXT_COLOR, fontSize: 11 },
        axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
        splitLine: { show: false },
        min: 0,
      },
    ],
    series: [
      {
        name: 'Published/s',
        type: 'line',
        yAxisIndex: 0,
        data: publishedPerSec,
        smooth: true,
        lineStyle: { color: COLOR_GREEN, width: 2 },
        itemStyle: { color: COLOR_GREEN },
        symbol: 'none',
      },
      {
        name: 'Subscribed/s',
        type: 'line',
        yAxisIndex: 0,
        data: subscribedPerSec,
        smooth: true,
        lineStyle: { color: COLOR_RED, width: 2 },
        itemStyle: { color: COLOR_RED },
        symbol: 'none',
      },
      {
        name: 'Latency (ms)',
        type: 'scatter',
        yAxisIndex: 1,
        data: latencyMs.map((v, i) => (v != null ? [timestamps[i], v] : null)).filter(Boolean),
        symbolSize: 4,
        itemStyle: { color: COLOR_BLUE, opacity: 0.8 },
      },
    ],
  }
})

// --- Chart 2: Hourly history area chart ---

const historyChartOption = computed(() => {
  const h = metricsHistory.value

  return {
    backgroundColor: 'transparent',
    tooltip: {
      trigger: 'axis',
      backgroundColor: TOOLTIP_BG,
      borderColor: TOOLTIP_BORDER,
      borderWidth: 1,
      textStyle: { color: TEXT_COLOR, fontSize: 12 },
    },
    legend: {
      data: ['Publish OK', 'Subscribe OK', 'Publish Failed', 'Subscribe Failed'],
      textStyle: { color: TEXT_COLOR },
      top: 8,
    },
    grid: {
      left: 56,
      right: 16,
      top: 48,
      bottom: 48,
      containLabel: false,
    },
    dataZoom: [
      {
        type: 'inside',
        start: 0,
        end: 100,
      },
    ],
    xAxis: {
      type: 'category',
      data: h.dayHour,
      axisLabel: { color: TEXT_COLOR, fontSize: 11, rotate: 45 },
      axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
      splitLine: { show: false },
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: TEXT_COLOR, fontSize: 11 },
      axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
      splitLine: { lineStyle: { color: GRID_LINE_COLOR, type: 'dashed' } },
      min: 0,
    },
    series: [
      {
        name: 'Publish OK',
        type: 'line',
        stack: 'total',
        data: h.publishSucceeded,
        smooth: true,
        areaStyle: { color: `${COLOR_GREEN}33` },
        lineStyle: { color: COLOR_GREEN, width: 2 },
        itemStyle: { color: COLOR_GREEN },
        symbol: 'none',
      },
      {
        name: 'Subscribe OK',
        type: 'line',
        stack: 'total',
        data: h.subscribeSucceeded,
        smooth: true,
        areaStyle: { color: `${COLOR_BLUE}33` },
        lineStyle: { color: COLOR_BLUE, width: 2 },
        itemStyle: { color: COLOR_BLUE },
        symbol: 'none',
      },
      {
        name: 'Publish Failed',
        type: 'line',
        stack: 'total',
        data: h.publishFailed,
        smooth: true,
        areaStyle: { color: `${COLOR_RED}33` },
        lineStyle: { color: COLOR_RED, width: 2 },
        itemStyle: { color: COLOR_RED },
        symbol: 'none',
      },
      {
        name: 'Subscribe Failed',
        type: 'line',
        stack: 'total',
        data: h.subscribeFailed,
        smooth: true,
        areaStyle: { color: `${COLOR_AMBER}33` },
        lineStyle: { color: COLOR_AMBER, width: 2 },
        itemStyle: { color: COLOR_AMBER },
        symbol: 'none',
      },
    ],
  }
})

// --- Chart 3: Pie chart ---

const pieChartOption = computed(() => {
  const s = stats.value

  return {
    backgroundColor: 'transparent',
    tooltip: {
      trigger: 'item',
      backgroundColor: TOOLTIP_BG,
      borderColor: TOOLTIP_BORDER,
      borderWidth: 1,
      textStyle: { color: TEXT_COLOR, fontSize: 12 },
      formatter: '{b}: {c} ({d}%)',
    },
    legend: {
      orient: 'vertical',
      left: 'left',
      textStyle: { color: TEXT_COLOR },
      top: 'middle',
    },
    series: [
      {
        type: 'pie',
        radius: ['40%', '70%'],
        center: ['60%', '50%'],
        data: [
          { value: s.publishedSucceeded, name: 'Publish Success', itemStyle: { color: COLOR_GREEN } },
          { value: s.publishedFailed, name: 'Publish Failed', itemStyle: { color: COLOR_RED } },
          { value: s.receivedSucceeded, name: 'Subscribe Success', itemStyle: { color: COLOR_BLUE } },
          { value: s.receivedFailed, name: 'Subscribe Failed', itemStyle: { color: COLOR_AMBER } },
        ],
        emphasis: {
          itemStyle: {
            shadowBlur: 10,
            shadowOffsetX: 0,
            shadowColor: 'rgba(0,0,0,0.5)',
          },
        },
        label: {
          color: TEXT_COLOR,
          fontSize: 12,
        },
        labelLine: { lineStyle: { color: 'rgba(255,255,255,0.3)' } },
      },
    ],
  }
})
</script>

<style scoped>
.messaging-charts {
  width: 100%;
}

.chart-card {
  background: rgba(30, 30, 30, 0.8) !important;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.chart-title {
  display: flex;
  align-items: center;
  font-size: 1rem !important;
  font-weight: 600 !important;
  color: #e0e0e0;
}

.chart-body {
  padding: 8px 16px 16px !important;
  height: 340px;
}

.chart-body--pie {
  height: 300px;
}

.chart {
  height: 100% !important;
  width: 100% !important;
}
</style>
