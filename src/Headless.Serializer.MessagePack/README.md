# Headless.Serializer.MessagePack

MessagePack binary serialization implementation of `IBinarySerializer`.

## Problem Solved

Provides compact binary serialization for high-throughput scenarios (cache entries, internal message envelopes) where JSON text overhead is a bottleneck. Contractless by default — no `[MessagePackObject]` or `[Key]` attributes required.

## Key Features

- `MessagePackSerializer` — `IBinarySerializer` implementation
- Contractless by default: uses `ContractlessStandardResolver`, so plain POCOs serialize without any attributes
- Accepts `MessagePackSerializerOptions` via constructor for compression, custom resolvers, or security settings
- Applies `MessagePackSecurity.UntrustedData` by default; pass `untrustedData: false` only for trusted payloads where the MessagePack-CSharp fast path is intentional
- Built-in LZ4 compression available via `WithCompression(MessagePackCompression.Lz4BlockArray)`
- Full `ISerializer` surface via MessagePack's native buffer APIs — `Serialize(IBufferWriter<byte>)`, `Deserialize(ReadOnlyMemory<byte>)` / `Deserialize(in ReadOnlySequence<byte>)` — avoiding the buffer-copy overhead of its `Stream` overloads

## Design Notes

The parameterless constructor uses `MessagePackSecurity.UntrustedData` so default deserialization is safe for cross-service caches, external message producers, and other payloads outside the current process trust boundary. For trusted payloads where the MessagePack-CSharp fast path is intentional, construct with `untrustedData: false` or supply custom `MessagePackSerializerOptions` with the desired `Security`. When you pass options, the serializer uses them verbatim and you own the security level.

## Installation

```bash
dotnet add package Headless.Serializer.MessagePack
```

## Quick Start

```csharp
// Default: contractless, no compression, MessagePackSecurity.UntrustedData:
builder.Services.AddSingleton<IBinarySerializer, MessagePackSerializer>();

// Trusted payload fast path only when the trust boundary is explicit:
builder.Services.AddSingleton<IBinarySerializer>(new MessagePackSerializer(untrustedData: false));

// With LZ4 compression:
var options = MessagePackSerializerOptions
    .Standard.WithResolver(ContractlessStandardResolver.Instance)
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

// The parameterless constructor already applies this security level:
var options = MessagePackSerializerOptions
    .Standard.WithResolver(ContractlessStandardResolver.Instance)
    .WithSecurity(MessagePackSecurity.UntrustedData);

// Equivalent, without hand-building options:
var serializer = new MessagePackSerializer(untrustedData: true);
```

## Dependencies

- `Headless.Serializer.Abstractions`
- `MessagePack`

## Side Effects

None. Registration is fully explicit.
