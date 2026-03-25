---
domain: Serialization
packages: Serializer.Abstractions, Serializer.Json, Serializer.MessagePack
---

# Serialization

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Serializer.Abstractions](#headlessserializerabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.Serializer.Json](#headlessserializerjson)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Usage](#usage-1)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.Serializer.MessagePack](#headlessserializermessagepack)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Usage](#usage-2)
  - [Configuration](#configuration-2)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)

> Provider-agnostic serialization contracts with System.Text.Json and MessagePack implementations for text and binary formats.

## Quick Orientation
- Install `Headless.Serializer.Abstractions` to depend on interfaces only (e.g., in domain/application layers).
- Install `Headless.Serializer.Json` for JSON serialization via System.Text.Json. Register `SystemJsonSerializer` as `IJsonSerializer`.
- Install `Headless.Serializer.MessagePack` for compact binary serialization. Register `MessagePackSerializer` as `IBinarySerializer`.
- Interface hierarchy: `ISerializer` (base, Stream-based) -> `ITextSerializer` / `IBinarySerializer` (markers) -> `IJsonSerializer` (JSON-specific).
- Customize JSON behavior by implementing `IJsonOptionsProvider` with separate serialize/deserialize options.
- MessagePack uses contractless serialization by default (no `[Key]`/`[MessagePackObject]` attributes needed).

## Agent Instructions
- Always code against `ISerializer`, `IJsonSerializer`, or `IBinarySerializer` from Abstractions. Never reference `SystemJsonSerializer` or `MessagePackSerializer` directly in application code.
- Default to `Serializer.Json` (System.Text.Json) for general use. Use `Serializer.MessagePack` only when binary performance or payload size matters (caching, messaging, high-throughput scenarios).
- Register JSON: `services.AddSingleton<IJsonSerializer, SystemJsonSerializer>()`. Optionally register a custom `IJsonOptionsProvider` before it.
- Register MessagePack: `services.AddSingleton<IBinarySerializer, MessagePackSerializer>()`. Pass custom `MessagePackSerializerOptions` via constructor for compression or resolver changes.
- Built-in JSON converters available in Serializer.Json: `UnixTimeJsonConverter`, `IpAddressJsonConverter`, `EmptyStringAsNullJsonConverter`, `StringToGuidJsonConverter`, `SingleOrCollectionJsonConverter`. Use them in your `IJsonOptionsProvider`.
- All serialization is Stream-based. Use extension methods for convenience patterns.
- None of these packages register side effects — you must explicitly register the serializer implementations in DI.

---
# Headless.Serializer.Abstractions

Defines unified interfaces for serialization and deserialization.

## Problem Solved

Provides provider-agnostic serialization contracts supporting both text (JSON) and binary formats, enabling consistent serialization patterns across the application without coupling to specific implementations.

## Key Features

- `ISerializer` - Base serialization interface with Stream-based operations
- `IBinarySerializer` - Marker interface for binary serializers
- `ITextSerializer` - Marker interface for text-based serializers
- `IJsonSerializer` - Specific interface for JSON serialization
- Extension methods for common serialization patterns

## Installation

```bash
dotnet add package Headless.Serializer.Abstractions
```

## Usage

```csharp
public sealed class DataService(IJsonSerializer serializer)
{
    public T? Load<T>(Stream stream)
    {
        return serializer.Deserialize<T>(stream);
    }

    public void Save<T>(T data, Stream stream)
    {
        serializer.Serialize(data, stream);
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
---
# Headless.Serializer.Json

System.Text.Json implementation of the serializer abstractions.

## Problem Solved

Provides high-performance JSON serialization using System.Text.Json with customizable options, converters, and type info resolvers for flexible JSON handling.

## Key Features

- `SystemJsonSerializer` - IJsonSerializer implementation
- `IJsonOptionsProvider` - Customizable serialization options
- Built-in converters:
  - `UnixTimeJsonConverter` - Unix timestamp handling
  - `IpAddressJsonConverter` - IP address serialization
  - `EmptyStringAsNullJsonConverter` - Null handling
  - `StringToGuidJsonConverter` - GUID parsing
  - `SingleOrCollectionJsonConverter` - Array/single value handling
- Type info resolvers and modifiers

## Installation

```bash
dotnet add package Headless.Serializer.Json
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register with default options
builder.Services.AddSingleton<IJsonSerializer, SystemJsonSerializer>();

// Or with custom options provider
builder.Services.AddSingleton<IJsonOptionsProvider, CustomJsonOptionsProvider>();
builder.Services.AddSingleton<IJsonSerializer, SystemJsonSerializer>();
```

## Usage

```csharp
public sealed class ApiClient(IJsonSerializer serializer)
{
    public async Task<T?> GetAsync<T>(HttpClient client, string url)
    {
        await using var stream = await client.GetStreamAsync(url);
        return serializer.Deserialize<T>(stream);
    }
}
```

## Configuration

Implement `IJsonOptionsProvider` for custom options:

```csharp
public sealed class CustomJsonOptionsProvider : IJsonOptionsProvider
{
    public JsonSerializerOptions GetSerializeOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public JsonSerializerOptions GetDeserializeOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };
}
```

## Dependencies

- `Headless.Serializer.Abstractions`

## Side Effects

None.
---
# Headless.Serializer.MessagePack

MessagePack binary serialization implementation.

## Problem Solved

Provides compact, high-performance binary serialization using MessagePack format, ideal for caching, messaging, and scenarios requiring smaller payload sizes than JSON.

## Key Features

- `MessagePackSerializer` - IBinarySerializer implementation
- Contractless serialization by default (no attributes required)
- Configurable MessagePackSerializerOptions
- Smaller payloads than JSON
- Faster serialization/deserialization than text formats

## Installation

```bash
dotnet add package Headless.Serializer.MessagePack
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register with default options (contractless)
builder.Services.AddSingleton<IBinarySerializer, MessagePackSerializer>();
```

## Usage

```csharp
public sealed class CacheService(IBinarySerializer serializer)
{
    public byte[] Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        serializer.Serialize(value, stream);
        return stream.ToArray();
    }

    public T? Deserialize<T>(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return serializer.Deserialize<T>(stream);
    }
}
```

## Configuration

Custom options can be provided via constructor:

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithResolver(ContractlessStandardResolver.Instance)
    .WithCompression(MessagePackCompression.Lz4BlockArray);

builder.Services.AddSingleton<IBinarySerializer>(
    new MessagePackSerializer(options)
);
```

## Dependencies

- `Headless.Serializer.Abstractions`
- `MessagePack`

## Side Effects

None.
