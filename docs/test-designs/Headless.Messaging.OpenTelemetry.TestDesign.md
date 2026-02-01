# Test Case Design: Headless.Messaging.OpenTelemetry

**Package:** `src/Headless.Messaging.OpenTelemetry`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

OpenTelemetry instrumentation for tracing and metrics.

| File | Type | Priority |
|------|------|----------|
| `MessagingInstrumentation.cs` | Instrumentation | P1 |
| `MessagingDiagnosticSourceSubscriber.cs` | DiagnosticSource listener | P1 |
| `DiagnosticListener.cs` | Listener impl | P1 |
| `MessagingMetrics.cs` | Metrics | P2 |
| `MetricsSetup.cs` | Metrics registration | P2 |
| `Setup.cs` | DI registration | P2 |

## Test Recommendation

**Create: `Headless.Messaging.OpenTelemetry.Tests.Unit`**

### Unit Tests Needed

#### MessagingInstrumentation Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_activity_on_publish` | P1 | Publish tracing |
| `should_create_activity_on_consume` | P1 | Consume tracing |
| `should_propagate_trace_context` | P1 | Context propagation |
| `should_set_activity_tags` | P1 | Tag attributes |
| `should_record_exception_on_error` | P1 | Error recording |
| `should_set_activity_status_on_success` | P2 | Success status |

#### MessagingDiagnosticSourceSubscriber Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_subscribe_to_diagnostic_source` | P1 | Subscription |
| `should_handle_BeforePublish_event` | P1 | Publish start |
| `should_handle_AfterPublish_event` | P1 | Publish end |
| `should_handle_ErrorPublish_event` | P1 | Publish error |
| `should_handle_BeforeConsume_event` | P1 | Consume start |
| `should_handle_AfterConsume_event` | P1 | Consume end |
| `should_handle_ErrorConsume_event` | P1 | Consume error |

#### MessagingMetrics Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_increment_publish_counter` | P2 | Publish count |
| `should_increment_consume_counter` | P2 | Consume count |
| `should_record_publish_duration` | P2 | Latency histogram |
| `should_record_consume_duration` | P2 | Latency histogram |
| `should_track_failure_counts` | P2 | Error metrics |

#### Setup Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_register_instrumentation` | P2 | DI setup |
| `should_configure_tracer_provider` | P2 | Tracer config |
| `should_configure_meter_provider` | P2 | Meter config |

### Integration Tests Needed

**Create: `Headless.Messaging.OpenTelemetry.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_create_traces_for_publish_consume` | P1 | End-to-end tracing |
| `should_link_publish_consume_spans` | P1 | Span linking |
| `should_export_to_in_memory_exporter` | P2 | Export verification |

## Test Infrastructure

```csharp
// Use in-memory exporter for testing
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("Headless.Messaging")
    .AddInMemoryExporter(activities)
    .Build();

var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter("Headless.Messaging")
    .AddInMemoryExporter(metrics)
    .Build();
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 6 |
| **Required Unit Tests** | **~20 cases** (new project) |
| **Required Integration Tests** | **~5 cases** (new project) |
| Priority | P2 - Observability |

**Note:** Proper tracing is critical for debugging distributed systems. Ensure trace context propagation works correctly across publish/consume boundaries.
