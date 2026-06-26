# Headless.Messaging.OpenTelemetry

OpenTelemetry instrumentation for distributed tracing and metrics in the messaging system.

## Problem Solved

Provides automatic tracing spans, metrics, and context propagation for message publishing and consumption to enable end-to-end observability across distributed systems.

## Key Features

- **Distributed Tracing**: Automatic span creation for publish/consume operations
- **Context Propagation**: W3C Trace Context header injection/extraction
- **Metrics**: Message throughput, latency, failures, and retry counts
- **Correlation**: Links messages to originating HTTP requests and spans
- **Standard Export**: Compatible with Jaeger, Zipkin, Prometheus, and other backends

## Installation

```bash
dotnet add package Headless.Messaging.OpenTelemetry
```

## Quick Start

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddMessagingInstrumentation().AddJaegerExporter());

builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");
    options.UseRabbitMq(config);
});
```

## Configuration

The messaging instrumentation is configured on the `TracerProviderBuilder`, not on `MessagingOptions`:

```csharp
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing =>
        tracing
            .AddMessagingInstrumentation(opt =>
            {
                opt.EnableMetrics = true; // collect message metrics in addition to traces
                opt.SuppressTenantIdTag = false; // set true for shared multi-tenant trace backends
                opt.SuppressIntentTags = false; // set true to omit intent and destination-kind tags
                opt.SuppressRetryCountTag = false; // set true to omit headless.messaging.retry_count

                opt.AddEnricher(new MyCustomEnricher()); // implements IActivityTagEnricher
            })
            .AddJaegerExporter()
    );
```

Custom enrichers implement `IActivityTagEnricher` from `Headless.Messaging.OpenTelemetry`. The pipeline runs built-in enrichers first (`TenantIdTagEnricher`, `IntentTagEnricher`, then `RetryCountTagEnricher`, each gated by its suppression option) and then custom enrichers in registration order. Enricher exceptions are isolated and logged; the messaging operation continues regardless.

Built-in messaging tags:

| Tag | Value | Suppression |
| --- | --- | --- |
| `headless.messaging.intent` | `bus` or `queue` | `SuppressIntentTags` |
| `messaging.destination.kind` | `topic` for bus, `queue` for queue | `SuppressIntentTags` |
| `headless.messaging.tenant_id` | Tenant header value | `SuppressTenantIdTag` |
| `headless.messaging.retry_count` | Persisted retry pickup count | `SuppressRetryCountTag` |

## Dependencies

- `Headless.Messaging.Core`
- `OpenTelemetry.Api`
- `OpenTelemetry.Extensions.Hosting`

## Side Effects

- Creates tracing spans for all message operations
- Injects W3C Trace Context headers into messages
- Exports telemetry to configured exporters
