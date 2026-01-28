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
