# Headless.Serializer.MessagePack

MessagePack binary serialization implementation of `IBinarySerializer`.

## Problem Solved

Provides compact binary serialization for high-throughput scenarios (cache entries, internal message envelopes) where JSON text overhead is a bottleneck. Contractless by default — no `[MessagePackObject]` or `[Key]` attributes required.

## Key Features

- `MessagePackSerializer` — `IBinarySerializer` implementation
- Contractless by default: uses `ContractlessStandardResolver`, so plain POCOs serialize without any attributes
- Accepts `MessagePackSerializerOptions` via constructor for compression, custom resolvers, or security settings
- Built-in LZ4 compression available via `WithCompression(MessagePackCompression.Lz4BlockArray)`
- Full `ISerializer` surface: generic `Serialize<T>` / `Deserialize<T>`, non-generic `Serialize(object?)` / `Deserialize(Stream, Type)`

## Installation

```bash
dotnet add package Headless.Serializer.MessagePack
```

## Quick Start

```csharp
// Default: contractless, no compression:
builder.Services.AddSingleton<IBinarySerializer, MessagePackSerializer>();

// With LZ4 compression:
var options = MessagePackSerializerOptions.Standard
    .WithResolver(ContractlessStandardResolver.Instance)
    .WithCompression(MessagePackCompression.Lz4BlockArray);

builder.Services.AddSingleton<IBinarySerializer>(new MessagePackSerializer(options));

// Consume via abstraction — use extension helpers to avoid Stream boilerplate:
public sealed class CacheWriter(IBinarySerializer serializer)
{
    public byte[]? Serialize<T>(T value) => serializer.SerializeToBytes(value);

    public T? Deserialize<T>(byte[] data) => serializer.Deserialize<T>(data);
}
```

## Configuration

All configuration is passed via `MessagePackSerializerOptions` at construction time:

```csharp
// Switch to attribute-based (non-contractless) mode:
var options = MessagePackSerializerOptions.Standard; // requires [MessagePackObject]/[Key] attributes

// Security: disallow deserialization of arbitrary types via typeless API:
var options = MessagePackSerializerOptions.Standard
    .WithResolver(ContractlessStandardResolver.Instance)
    .WithSecurity(MessagePackSecurity.UntrustedData);
```

## Dependencies

- `Headless.Serializer.Abstractions`
- `MessagePack`

## Side Effects

None. Registration is fully explicit.
