---
title: "refactor: Complete Messaging Dashboard alignment with Jobs Dashboard"
type: refactor
date: 2026-03-16
origin: (inline brainstorm — Dashboard Alignment Jobs + Messaging, 2026-03-16)
---

> **Verification gate:** Before claiming any task or story complete — build the frontend (`npm run build` in `src/Headless.Messaging.Dashboard/wwwroot/`) and confirm zero errors. Do not mark complete based on reading code alone.

# Complete Messaging Dashboard Alignment

## Overview

PR #192 rewrote the Messaging Dashboard frontend to Vue 3/TS/Vuetify 3 and refactored backend endpoints to Minimal API. A thorough comparison of the old (Vue 2/Bootstrap) and new dashboards reveals **15 missing features** spanning charts, detail views, navigation, node management, and cleanup:

**Charts & Metrics:**
- No charts/graphs — Dashboard shows raw `JSON.stringify()` metrics and a flat history table (old had 2 uPlot charts)

**Message Detail & Error Handling:**
- Minimal message detail view — only renders `Content` as JSON, no metadata
- No structured exception/stack trace rendering for failed messages (Jobs Dashboard has full exception dialog with collapsible stack trace, source, help link, inner exception, raw data)
- No `json-bigint` support — precision loss for message IDs > 2^53

**Navigation & Layout:**
- No badge counts on nav links (old had: publishedFailed, receivedFailed, subscribers, servers)
- Footer only shows "2025 — Headless Framework" — no system info, no switched node indicator
- No 404 catch-all route

**Node Management (K8s):**
- No K8s namespace selector dropdown (old had namespace dropdown + per-namespace service listing)
- No "Ping All" button
- No active node highlighting in table

**Published/Received UX:**
- No status counts on tabs (old had badge counts per status)
- Delayed status missing dynamic labels ("Expires At" should → "Delayed Publish Time", "Requeue" → "Immediately Publish")
- No refresh controls on Published/Received pages

**Subscribers:**
- No C# method signature syntax highlighting (old had color-coded type/keyword/string spans)

**Dead Code & Cleanup:**
- vue-i18n installed but zero `$t()` calls — translations registered but never used
- Tailwind CSS installed + imported but zero Tailwind classes used — only Vuetify utilities
- `auth.config.ts` exported but never imported anywhere
- `ApiResponse`/`PaginatedResponse`/`ErrorResponse` types declared but never imported

## Problem Statement / Motivation

The migration stopped at structural scaffolding. The monitoring value — charts, rich message inspection, exception debugging, K8s node management — is missing. Users cannot visualize throughput trends, debug failed messages with stack traces, or manage K8s node topology as they could with the old dashboard.

## Proposed Solution

Complete the alignment in 14 stories:

1. **Cleanup** — Remove dead code: i18n, Tailwind, unused types/configs, old assets
2. **Footer** — System info (messaging/broker/storage) + switched node indicator + fixed height
3. **404 route** — Catch-all for unknown paths
4. **Nav badges** — Badge counts on navigation links (failed counts, subscriber/server counts)
5. **Messaging store** — Pinia store for dashboard state persistence across navigation
6. **Charts** — ECharts real-time line chart, hourly history chart, success/failure pie chart
7. **Backend enrichment** — Message detail endpoints return full DTO, not just Content string
8. **ConfirmDialog** — Port `isException`/`isCode` modes with structured exception rendering from Jobs
9. **MessageDetailDialog** — Metadata header, exception rendering for failed messages, copy-to-clipboard, json-bigint
10. **Stat cards** — Surface Subscribers + PublishedDelayed counts
11. **Published/Received UX** — Status tab counts, delayed dynamic labels, refresh controls
12. **Nodes K8s** — Namespace selector, ping all, active node highlighting
13. **Subscriber syntax highlighting** — C# method signatures with color-coded spans
14. **Frontend build + cleanup verification** — Final build, remove dist artifacts if stale

(see brainstorm: inline brainstorm — key decisions: align don't merge, keep polling, no shared frontend components yet, duplicate Vue 3 stack)

## Technical Considerations

- **Chart library**: ECharts via `vue-echarts` (matching Jobs Dashboard pattern from `TimeJobCharts.vue`)
- **Dark theme charts**: Match Jobs palette — `transparent` bg, `#e0e0e0` text, `rgba(42, 42, 42, 0.95)` tooltips, `rgba(255,255,255,0.05)` grid lines
- **Real-time data**: `/api/metrics-realtime` returns `CircularBuffer<int?>[4]` — 300 data points at 1s granularity (timestamps, published/s, subscribed/s, latency)
- **History data**: `/api/metrics-history` returns hourly aggregates — note "Successed" typo from upstream, keep as-is for compatibility
- **Message detail endpoint**: Currently returns `message.Content` (string only). Needs full DTO with metadata
- **json-bigint**: Required for message content parsing — message IDs can exceed `Number.MAX_SAFE_INTEGER`
- **Exception rendering**: Port Jobs `ConfirmDialog` pattern — structured exception parsing (Message, StackTrace, Source, HelpLink, Data, InnerException) with collapsible sections, then use from both ConfirmDialog and MessageDetailDialog
- **K8s namespace**: Old dashboard fetched `/list-ns` and `/list-svc/{namespace}` — these endpoints already exist in backend
- **No SignalR**: Per brainstorm, keep polling. Migrate to SignalR separately
- **Tailwind removal**: Not used — only Vuetify utility classes. Remove package + config + `@tailwind` directives from main.css

## System-Wide Impact

- **Frontend bundle**: Adding `echarts` + `vue-echarts` increases size, but removing `vue-i18n` + `tailwindcss` partially offsets. Use tree-shaking for ECharts
- **Backend API change**: Enriching message detail endpoint is non-breaking (new fields alongside existing `Content`)
- **No cross-package impact**: All changes scoped to `Headless.Messaging.Dashboard` and its `wwwroot/`
- **Cookie contract**: Node switching cookies (`messaging.node`, `messaging.node.ns`) match old dashboard — no backend changes needed

## Stories

> Full story details in companion PRD: [`2026-03-16-refactor-complete-messaging-dashboard-alignment-plan.prd.json`](./2026-03-16-refactor-complete-messaging-dashboard-alignment-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | Remove dead code: i18n, Tailwind, unused types/configs | S |
| US-002 | Redesign footer — system info + switched node + fixed height | S |
| US-003 | Add 404 catch-all route | XS |
| US-004 | Add badge counts to navigation links | S |
| US-005 | Create messagingStore (Pinia) for dashboard state | S |
| US-006 | Add ECharts dashboard charts (line + pie) | L |
| US-007 | Enrich message detail backend endpoints | S |
| US-008 | Port ConfirmDialog exception/code rendering from Jobs | M |
| US-009 | Enhance MessageDetailDialog — metadata, exceptions, json-bigint | M |
| US-010 | Add missing stat cards (Subscribers, PublishedDelayed) | S |
| US-011 | Published/Received UX — tab counts, delayed labels, refresh | M |
| US-012 | Nodes page — K8s namespace selector, ping all, active highlight | M |
| US-013 | Subscriber method syntax highlighting | S |
| US-014 | Final build verification and dist cleanup | XS |

## Success Metrics

- Dashboard page renders 3 chart types (real-time line, history line, status pie) with auto-updating data
- Message detail dialog shows full metadata + structured exception rendering for failed messages with collapsible stack trace, source, help link, raw data sections
- System info + switched node indicator displayed in fixed-height footer on all pages
- Nav links show badge counts for failed messages, subscribers, and servers
- K8s namespace selector, ping all, and active node highlighting work on Nodes page
- Published/Received tabs show status counts and delayed status uses dynamic labels
- Subscriber methods render with C# syntax highlighting
- No vue-i18n, Tailwind, or dead code remains in source
- `npm run build` completes with zero errors

## Dependencies & Risks

- **ECharts bundle size**: Mitigate with tree-shaking imports (only CanvasRenderer, LineChart, PieChart, required components)
- **json-bigint bundle**: Small library (~5KB), justified by preventing precision loss on large message IDs
- **Backend "Successed" typo**: Leave as-is — upstream CAP library. Frontend handles both casings
- **K8s namespace endpoints**: Already exist in backend (`/list-ns`, `/list-svc/{ns}`) — no backend changes needed
- **Exception data format**: Backend may serialize exceptions as PascalCase (`Message`, `StackTrace`) or camelCase (`message`, `stackTrace`) — exception parser must handle both (Jobs pattern already does this)

## Sources & References

- **Origin brainstorm**: Inline brainstorm "Dashboard Alignment (Jobs + Messaging)" — key decisions: align-don't-merge, Vue 3/TS/Vuetify, keep polling, no shared frontend components
- **Jobs Dashboard charts**: `src/Headless.Jobs.Dashboard/wwwroot/src/components/TimeJobCharts.vue`
- **Jobs Dashboard exception rendering**: `src/Headless.Jobs.Dashboard/wwwroot/src/components/common/ConfirmDialog.vue` (lines 22-44: type defs, 77-195: parsing, 198-305: template, 307-648: styles)
- **Messaging endpoints**: `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`
- **Metrics data source**: `src/Headless.Messaging.Dashboard/MessagingMetricsEventListener.cs`
- **Dashboard merge proposal**: `docs/jobs-messaging-dashboard-proposal.md`
- PR #192: `refactor: align Messaging Dashboard with Jobs Dashboard stack`
