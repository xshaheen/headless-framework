# Framework.Messaging.Foundatio

Foundatio-based message bus implementation with in-memory and Redis support.

## Problem Solved

Provides message bus implementations using Foundatio library, supporting in-memory messaging for development/testing and Redis for production distributed messaging.

## Key Features

- `FoundatioMessageBusAdapter` - Adapter bridging Foundatio to framework interfaces
- In-memory message bus for development
- Redis message bus for production (via Foundatio.Redis)
- JSON serialization via framework serializer

## Installation

```bash
dotnet add package Framework.Messaging.Foundatio
```

## Quick Start

### In-Memory (Development)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFoundatioInMemoryMessageBus();
```

### Redis (Production)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFoundatioRedisMessageBus(options =>
    options.ConnectionString(builder.Configuration.GetConnectionString("Redis"))
);
```

## Configuration

### In-Memory Options

```csharp
services.AddFoundatioInMemoryMessageBus(builder =>
    builder.AcknowledgementTimeout(TimeSpan.FromSeconds(30))
);
```

## Dependencies

- `Framework.Messaging.Abstractions`
- `Framework.Serializer.Json`
- `Foundatio`

## Side Effects

- Registers `IMessageBus` as singleton
- Registers `IMessagePublisher` as singleton
- Registers `IMessageSubscriber` as singleton
