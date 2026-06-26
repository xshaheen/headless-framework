# Headless.Serializer.Abstractions

Defines unified interfaces and extension methods for serialization and deserialization.

## Problem Solved

Provides provider-agnostic serialization contracts for both text (JSON) and binary formats, enabling consistent serialization patterns across application layers without coupling to a specific implementation.

## Key Features

- `ISerializer` — core buffer-first interface: `Serialize<T>`/`Serialize` write to an `IBufferWriter<byte>`; `Deserialize<T>`/`Deserialize` read from a `ReadOnlyMemory<byte>` or `ReadOnlySequence<byte>`
- `ITextSerializer` — marker interface for text-format implementations
- `IJsonSerializer : ITextSerializer` — JSON-specific marker; register JSON providers against this
- `IBinarySerializer : ISerializer` — marker interface for binary implementations
- `SerializerExtensions` — C# 14 extension members on `ISerializer` adapting the core buffer contract to common shapes:
  - `Deserialize<T>(byte[])` — deserialize from a byte array (read in place via `ReadOnlyMemory<byte>`)
  - `Deserialize<T>(string?)` — deserialize from a string (UTF-8 for text, Base64 for binary); `null` returns `default`
  - `Deserialize<T>(Stream)` — deserialize from a stream
  - `SerializeToBytes<T>(T?)` — serialize to `byte[]`, null-safe
  - `SerializeToString<T>(T?)` — serialize to string (UTF-8 for text, Base64 for binary), null-safe
  - `Serialize<T>(T, Stream)` / `Serialize(object?, Stream)` — serialize to a stream

## Design Notes

The contract is **buffer-first, not Stream-first**. `IBufferWriter<byte>` and `ReadOnlyMemory<byte>` / `ReadOnlySequence<byte>` are the primitives both backends expose with the fewest copies (`System.Text.Json` via `Utf8JsonWriter`/`Utf8JsonReader`, MessagePack natively) — so a `SerializeToBytes` no longer pays for a `MemoryStream` plus its `ToArray()` copy, and a byte-array deserialize is read in place. `byte[]`, `string`, and `Stream` are provided as extension adapters because they are convenient at call sites, not because they are the fast path. When you already hold a contiguous buffer (a `byte[]`, a cache value segment), call the `ReadOnlyMemory<byte>` overload directly to avoid the adapter hop.

## Installation

```bash
dotnet add package Headless.Serializer.Abstractions
```

## Quick Start

```csharp
// Depend only on the abstraction in domain/application code:
public sealed class DataService(IJsonSerializer serializer)
{
    public T? Load<T>(Stream stream) => serializer.Deserialize<T>(stream);

    public void Save<T>(T data, Stream stream) => serializer.Serialize(data, stream);

    // Extension helpers — no Stream boilerplate needed:
    public byte[]? ToBytes<T>(T data) => serializer.SerializeToBytes(data);

    public T? FromBytes<T>(byte[] data) => serializer.Deserialize<T>(data);
}
```

## Configuration

None. Abstractions-only package.

## Dependencies

None.

## Side Effects

None.
