# Headless.Serializer.Abstractions

Defines unified interfaces and extension methods for serialization and deserialization.

## Problem Solved

Provides provider-agnostic serialization contracts for both text (JSON) and binary formats, enabling consistent serialization patterns across application layers without coupling to a specific implementation.

## Key Features

- `ISerializer` — core Stream-based interface with generic and non-generic overloads
- `ITextSerializer` — marker interface for text-format implementations
- `IJsonSerializer : ITextSerializer` — JSON-specific marker; register JSON providers against this
- `IBinarySerializer : ISerializer` — marker interface for binary implementations
- `SerializerExtensions` — C# 14 extension members on `ISerializer`:
  - `Deserialize<T>(byte[])` — deserialize from a byte array
  - `Deserialize<T>(string?)` — deserialize from a string (UTF-8 for text, Base64 for binary)
  - `SerializeToBytes<T>(T?)` — serialize to `byte[]`, null-safe
  - `SerializeToString<T>(T?)` — serialize to string (UTF-8 for text, Base64 for binary), null-safe

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
