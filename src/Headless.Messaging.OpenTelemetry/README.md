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
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Headless.Messaging")
        .AddJaegerExporter());

builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");
    options.UseRabbitMQ(config);
    options.UseOpenTelemetry();

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseOpenTelemetry(otel =>
{
    otel.EnrichPublisher = (activity, message) =>
    {
        activity?.SetTag("message.type", message.GetType().Name);
    };

    otel.EnrichConsumer = (activity, context) =>
    {
        activity?.SetTag("consumer.topic", context.Topic);
    };
});
```

## Dependencies

- `Headless.Messaging.Core`
- `OpenTelemetry.Api`
- `OpenTelemetry.Extensions.Hosting`

## Side Effects

- Creates tracing spans for all message operations
- Injects W3C Trace Context headers into messages
- Exports telemetry to configured exporters
