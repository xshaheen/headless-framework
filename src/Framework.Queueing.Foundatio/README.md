# Framework.Queueing.Foundatio

Foundatio-based queue implementation with in-memory and Redis support.

## Problem Solved

Provides queue implementations using Foundatio library, supporting in-memory queuing for development/testing and Redis/SQS/Azure for production distributed queuing.

## Key Features

- `QueueFoundatioAdapter<T>` - Adapter bridging Foundatio to framework interfaces
- In-memory queue for development
- Redis queue for production (via Foundatio.Redis)
- Queue behaviors and event integration
- Dead letter queue support

## Installation

```bash
dotnet add package Framework.Queueing.Foundatio
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// In-memory queue
builder.Services.AddSingleton<IQueue<OrderMessage>>(sp =>
    new QueueFoundatioAdapter<OrderMessage>(
        new InMemoryQueue<OrderMessage>()
    )
);
```

## Configuration

Uses Foundatio queue configuration. See Foundatio documentation for provider-specific options.

## Dependencies

- `Framework.Queueing.Abstractions`
- `Foundatio`

## Side Effects

None directly. Queue instances should be registered appropriately.
