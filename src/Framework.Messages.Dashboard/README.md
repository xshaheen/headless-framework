# Framework.Messages.Dashboard

Web-based dashboard for monitoring and managing distributed messaging infrastructure.

## Problem Solved

Provides real-time visibility into message processing, failures, retries, and system health through an embedded web UI for operations and troubleshooting.

## Key Features

- **Real-Time Monitoring**: Live message throughput and latency metrics
- **Message Explorer**: Search, filter, and inspect messages
- **Failure Management**: View and retry failed messages
- **Node Discovery**: Multi-instance cluster visibility
- **Performance Metrics**: Consumer processing stats and bottlenecks

## Installation

```bash
dotnet add package Framework.Messages.Dashboard
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");
    options.UseRabbitMQ(config);

    options.UseDashboard(dashboard =>
    {
        dashboard.PathMatch = "/messages";
        dashboard.StatsPollingInterval = 2000;
    });

    options.ScanConsumers(typeof(Program).Assembly);
});

// Access dashboard at: http://localhost:5000/messages
```

## Configuration

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.PathMatch = "/messages";
    dashboard.StatsPollingInterval = 2000;
    dashboard.Authorization = new[] { new CustomDashboardAuthFilter() };
});
```

## Dependencies

- `Framework.Messages.Core`
- Embedded web UI assets

## Side Effects

- Exposes web endpoint at configured path (default: `/messaging`)
- Periodically polls message storage for statistics
- Requires authentication configuration for production use
