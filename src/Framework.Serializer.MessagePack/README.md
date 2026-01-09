# Framework.Serializer.MessagePack

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
dotnet add package Framework.Serializer.MessagePack
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

- `Framework.Serializer.Abstractions`
- `MessagePack`

## Side Effects

None.
