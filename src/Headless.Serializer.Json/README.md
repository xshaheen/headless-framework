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
