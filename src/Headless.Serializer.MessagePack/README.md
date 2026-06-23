# Headless.Serializer.MessagePack

MessagePack binary serialization implementation of `IBinarySerializer`.

## Problem Solved

Provides compact binary serialization for high-throughput scenarios (cache entries, internal message envelopes) where JSON text overhead is a bottleneck. Contractless by default — no `[MessagePackObject]` or `[Key]` attributes required.

## Key Features

- `MessagePackSerializer` — `IBinarySerializer` implementation
- Contractless by default: uses `ContractlessStandardResolver`, so plain POCOs serialize without any attributes
- Accepts `MessagePackSerializerOptions` via constructor for compression, custom resolvers, or security settings
- `untrustedData` constructor flag opts into `MessagePackSecurity.UntrustedData` (recursion-depth limit + collision-resistant hashing) without hand-building options
- Built-in LZ4 compression available via `WithCompression(MessagePackCompression.Lz4BlockArray)`
- Full `ISerializer` surface via MessagePack's native buffer APIs — `Serialize(IBufferWriter<byte>)`, `Deserialize(ReadOnlyMemory<byte>)` / `Deserialize(in ReadOnlySequence<byte>)` — avoiding the buffer-copy overhead of its `Stream` overloads

## Design Notes

The parameterless constructor uses MessagePack-CSharp's trusted-data security default (`MessagePackSecurity.TrustedData`) — the fast path, appropriate when payloads originate inside the trust boundary (for example cache values the application itself wrote). When deserializing data from outside the trust boundary (a cache other services or attackers can write to, message payloads from external producers), construct with `untrustedData: true` to apply `MessagePackSecurity.UntrustedData` (recursion-depth limit + collision-resistant hashing that defends against hash-flooding and stack-overflow DoS). For finer control, supply your own `MessagePackSerializerOptions` — when you pass options the serializer uses them verbatim and you own the security level, so set a custom `Security` there rather than combining it with the `untrustedData` switch.

## Installation

```bash
dotnet add package Headless.Serializer.MessagePack
```

## Quick Start

```csharp
// Default: contractless, no compression, MessagePackSecurity.TrustedData (trusted input):
builder.Services.AddSingleton<IBinarySerializer, MessagePackSerializer>();

// Hardened for untrusted input (one flag — no hand-built options):
builder.Services.AddSingleton<IBinarySerializer>(new MessagePackSerializer(untrustedData: true));

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

// Security for untrusted input — the untrustedData flag is the shortcut for this:
var options = MessagePackSerializerOptions.Standard
    .WithResolver(ContractlessStandardResolver.Instance)
    .WithSecurity(MessagePackSecurity.UntrustedData);

// Equivalent, without hand-building options:
var serializer = new MessagePackSerializer(untrustedData: true);
```

## Dependencies

- `Headless.Serializer.Abstractions`
- `MessagePack`

## Side Effects

None. Registration is fully explicit.
