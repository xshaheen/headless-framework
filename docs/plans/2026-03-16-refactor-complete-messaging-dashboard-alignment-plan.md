---
title: "refactor: Complete Messaging Dashboard alignment with Jobs Dashboard"
type: refactor
date: 2026-03-16
origin: (inline brainstorm — Dashboard Alignment Jobs + Messaging, 2026-03-16)
---

> **Verification gate:** Before claiming any task or story complete — build the frontend (`npm run build` in `src/Headless.Messaging.Dashboard/wwwroot/`) and confirm zero errors. Do not mark complete based on reading code alone.

# Complete Messaging Dashboard Alignment

## Overview

PR #192 rewrote the Messaging Dashboard frontend to Vue 3/TS/Vuetify 3 and refactored backend endpoints to Minimal API. However, several key features are missing or incomplete compared to the Jobs Dashboard:

- **No charts/graphs** — Dashboard shows raw JSON metrics and a flat history table
- **Minimal message detail view** — only renders `Content` as JSON, no metadata/exception rendering
- **Unused i18n plumbing** — vue-i18n installed but only English needed
- **System info in wrong location** — should be in a fixed-height footer
- **No refresh controls** on Published/Received pages
- **Missing stat cards** — `Subscribers` and `PublishedDelayed` not surfaced
- **No 404 route** — unknown paths render blank
- **Backend returns only `Content` string** from message detail endpoints — frontend can't show metadata

## Problem Statement / Motivation

The Messaging Dashboard was rewritten to the Jobs stack (Vue 3/TS/Vuetify 3) but the migration stopped at structural scaffolding. The monitoring value — charts for throughput trends, rich message inspection, polished UX — is missing. Users monitoring messaging infrastructure cannot visualize metrics, inspect failed messages with context, or get a cohesive experience matching the Jobs Dashboard.

## Proposed Solution

Complete the alignment by adding:

1. ECharts-based charts (real-time metrics line chart, hourly history chart, success/failure pie chart)
2. Enhanced message detail dialog with metadata fields and structured exception rendering
3. Footer redesign with system info and fixed height
4. Dashboard state persistence via Pinia store
5. UX polish: refresh controls, missing stat cards, 404 route, i18n removal

(see brainstorm: inline brainstorm — key decisions: align don't merge, keep polling, no shared frontend components yet, duplicate Vue 3 stack)

## Technical Considerations

- **Chart library**: ECharts via `vue-echarts` (matching Jobs Dashboard pattern from `TimeJobCharts.vue`)
- **Dark theme**: Match Jobs Dashboard color palette — `transparent` bg, `#e0e0e0` text, `rgba(42, 42, 42, 0.95)` tooltips
- **Real-time data**: `/api/metrics-realtime` returns `CircularBuffer<int?>[4]` (timestamps, published/s, subscribed/s, latency) — 300 data points at 1s granularity
- **History data**: `/api/metrics-history` returns hourly aggregates (DayHour[], PublishSuccessed[], PublishFailed[], SubscribeSuccessed[], SubscribeFailed[]) — note "Successed" typo from upstream, keep as-is for compatibility
- **Message detail endpoint**: Currently returns `message.Content` (string only). Needs to return full DTO with metadata for the enhanced dialog
- **No SignalR**: Per brainstorm, keep polling. Migrate to SignalR as a separate follow-up
- **No i18n**: English only — remove `vue-i18n` dependency and locale files entirely

## System-Wide Impact

- **Frontend bundle size**: Adding `echarts` + `vue-echarts` increases bundle. Use tree-shaking (import only Line, Pie charts + required components)
- **Backend API change**: Enriching message detail endpoint response is a non-breaking addition (new fields alongside existing `Content`)
- **No cross-package impact**: Changes are scoped to `Headless.Messaging.Dashboard` and its `wwwroot/`

## Stories

> Full story details in companion PRD: [`2026-03-16-refactor-complete-messaging-dashboard-alignment-plan.prd.json`](./2026-03-16-refactor-complete-messaging-dashboard-alignment-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | Remove i18n/localization — English only | S |
| US-002 | Redesign footer — system info + fixed height | S |
| US-003 | Add 404 catch-all route | XS |
| US-004 | Create messagingStore (Pinia) for dashboard state | S |
| US-005 | Add ECharts dashboard charts (line + pie) | L |
| US-006 | Enrich message detail backend endpoint | S |
| US-007 | Enhance MessageDetailDialog with metadata + exceptions | M |
| US-008 | Add missing stat cards (Subscribers, PublishedDelayed) | S |
| US-009 | Add refresh controls to Published/Received pages | S |

## Success Metrics

- Dashboard page renders 3 chart types (real-time line, history line, status pie) with correct data
- Message detail dialog shows metadata fields + structured exception rendering for failed messages
- System info displayed in fixed-height footer on all pages
- No vue-i18n references remain in source
- All unknown routes redirect to 404 page
- Frontend builds with zero errors (`npm run build`)

## Dependencies & Risks

- **ECharts bundle size**: Mitigate with tree-shaking imports (only Line, Pie, required components)
- **Backend "Successed" typo**: Leave as-is — may come from upstream CAP library. Frontend already handles both casings
- **`CircularBuffer` data shape**: Need to verify exact response format from `MessagingMetricsEventListener.GetRealTimeMetrics()` to build correct chart data transformation

## Sources & References

- **Origin brainstorm**: Inline brainstorm "Dashboard Alignment (Jobs + Messaging)" — key decisions: align-don't-merge, Vue 3/TS/Vuetify, keep polling, no shared frontend components
- **Jobs Dashboard charts**: `src/Headless.Jobs.Dashboard/wwwroot/src/components/TimeJobCharts.vue`
- **Jobs Dashboard exception rendering**: `src/Headless.Jobs.Dashboard/wwwroot/src/components/common/ConfirmDialog.vue`
- **Messaging endpoints**: `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`
- **Metrics data source**: `src/Headless.Messaging.Dashboard/MessagingMetricsEventListener.cs`
- **Dashboard merge proposal**: `docs/jobs-messaging-dashboard-proposal.md`
- PR #192: `refactor: align Messaging Dashboard with Jobs Dashboard stack`
