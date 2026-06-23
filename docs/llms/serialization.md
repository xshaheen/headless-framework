---
domain: Serialization
packages: Serializer.Abstractions, Serializer.Json, Serializer.MessagePack
---

# Serialization

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [The `ISerializer` contract](#the-iserializer-contract)
    - [Interface hierarchy](#interface-hierarchy)
    - [Text vs binary trade-off](#text-vs-binary-trade-off)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Serializer.Abstractions](#headlessserializerabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Serializer.Json](#headlessserializerjson)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Serializer.MessagePack](#headlessserializermessagepack)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Provider-agnostic serialization contracts with System.Text.Json and MessagePack implementations for text and binary formats.

## Quick Orientation

Install `Headless.Serializer.Abstractions` to depend on interfaces only (domain/application layers). Add one provider package for each format needed:

- **JSON (default)**: `Headless.Serializer.Json` — System.Text.Json, human-readable, fully interoperable.
- **Binary**: `Headless.Serializer.MessagePack` — compact wire format, faster for high-throughput internal paths (caching, messaging).

Code against `ISerializer` / `IJsonSerializer` / `IBinarySerializer` from Abstractions. Never reference `SystemJsonSerializer` or `MessagePackSerializer` directly in service code.

Neither provider registers itself into DI automatically — you must call `services.AddSingleton<IJsonSerializer, SystemJsonSerializer>()` or `services.AddSingleton<IBinarySerializer, MessagePackSerializer>()` explicitly.

## Agent Instructions

- Always depend on `ISerializer`, `IJsonSerializer`, or `IBinarySerializer` from `Headless.Serializer.Abstractions`. Never reference `SystemJsonSerializer` or `MessagePackSerializer` in application code.
- Default to `Headless.Serializer.Json` for general use. Switch to `Headless.Serializer.MessagePack` only when binary performance or payload size matters (e.g., cache entries, internal message envelopes, high-throughput pipelines).
- Do not call `System.Text.Json.JsonSerializer` directly in application code — go through `IJsonSerializer` so the implementation can be swapped and options are centralized.
- Register JSON: `services.AddSingleton<IJsonSerializer, SystemJsonSerializer>()`. Register a custom `IJsonOptionsProvider` before the serializer if you need non-default options.
- Register MessagePack: `services.AddSingleton<IBinarySerializer, MessagePackSerializer>()`. Pass custom `MessagePackSerializerOptions` via constructor for compression or resolver changes.
- `SystemJsonSerializer` is annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` — not AOT-safe as-is. For AOT/NativeAOT scenarios, implement a source-generated `IJsonSerializer` instead.
- All serialization is Stream-based at the `ISerializer` level. Use extension methods (`SerializeToBytes<T>`, `SerializeToString<T>`, `Deserialize<T>(byte[])`, `Deserialize<T>(string?)`) from `SerializerExtensions` when you need byte arrays or strings.
- `SerializeToString` on a binary serializer (e.g., MessagePack) returns a Base64 string; on a text serializer it returns UTF-8. `Deserialize<T>(string?)` reverses this automatically.
- Built-in JSON converters in `Headless.Serializer.Json` — add them to your `IJsonOptionsProvider` when needed: `UnixTimeJsonConverter`, `IpAddressJsonConverter` (included in default options), `EmptyStringAsNullJsonConverter<T>`, `StringToGuidJsonConverter`, `NullableStringToGuidJsonConverter`, `StringToBooleanJsonConverter`, `SingleOrListJsonConverter<TItem>`, `SingleOrHashsetJsonConverter<TItem>`, `ObjectToInferredTypesJsonConverter`, `CollectionItemJsonConverter<TDatatype, TConverterType>`.
- The `DefaultWebJsonOptions` used by `DefaultJsonOptionsProvider` include `IpAddressJsonConverter` and `JsonStringEnumConverter(CamelCase)` automatically. Do not add them again in a custom provider — duplicate converters cause unexpected behavior.
- None of the serializer packages auto-register services — all DI wiring is explicit.

## Core Concepts

### The `ISerializer` contract

`ISerializer` is the root interface; all serialization flows through it:

```csharp
public interface ISerializer
{
    T? Deserialize<T>(Stream data);
    void Serialize<T>(T value, Stream output);
    object? Deserialize(Stream data, Type objectType);
    void Serialize(object? value, Stream output);
}
```

The base contract is Stream-in / Stream-out. Extension methods in `SerializerExtensions` (C# 14 extension members) add convenience overloads:

| Method | Input | Output | Note |
|--------|-------|--------|------|
| `Deserialize<T>(byte[])` | `byte[]` | `T?` | wraps in `MemoryStream` |
| `Deserialize<T>(string?)` | `string?` | `T?` | UTF-8 for text; Base64-decode for binary |
| `SerializeToBytes<T>(T?)` | `T?` | `byte[]?` | null-safe |
| `SerializeToString<T>(T?)` | `T?` | `string?` | UTF-8 for text; Base64 for binary |

### Interface hierarchy

```
ISerializer
├── ITextSerializer  (marker — text-format impls)
│   └── IJsonSerializer  (JSON-specific)
└── IBinarySerializer  (marker — binary-format impls)
```

`IBinarySerializer` and `ITextSerializer` are markers with no additional members. The distinction matters for the extension methods: `SerializeToString` returns a UTF-8 string when the serializer is `ITextSerializer`, and a Base64 string when it is `IBinarySerializer`.

### Text vs binary trade-off

| Dimension | JSON (`Serializer.Json`) | MessagePack (`Serializer.MessagePack`) |
|-----------|--------------------------|----------------------------------------|
| Human-readable | Yes | No |
| Interop (cross-language, REST, logging) | Excellent | Limited — requires MessagePack decoders |
| Payload size | Larger (text overhead) | Compact (binary encoding) |
| Serialization speed | Good | Faster for large objects |
| Attribute requirements | None (reflection-based) | None — contractless by default |
| AOT-safety | Requires source gen | Contractless resolver uses reflection |
| Default use case | APIs, configuration, storage | Cache entries, internal messaging, high-throughput |

## Choosing a Provider

| Provider | Use when | Avoid when |
|----------|----------|------------|
| `Headless.Serializer.Json` | API responses, external interop, human-readable storage, logging, configuration, general default | Payload size or serialization throughput is the bottleneck; wire format is purely internal |
| `Headless.Serializer.MessagePack` | Internal cache entries, high-throughput message envelopes, storage where wire size matters | The consumer is a browser, third-party API, or any non-.NET client; debugging serialized bytes directly |

Both providers can coexist — register `IJsonSerializer` and `IBinarySerializer` independently. Components declare the specific interface they need.

---
## Headless.Serializer.Abstractions

Defines unified interfaces and extension methods for serialization and deserialization.

### Problem Solved

Provides provider-agnostic serialization contracts for both text (JSON) and binary formats, enabling consistent serialization patterns across application layers without coupling to a specific implementation.

### Key Features

- `ISerializer` — core Stream-based interface with generic and non-generic overloads
- `ITextSerializer` — marker interface for text-format implementations
- `IJsonSerializer : ITextSerializer` — JSON-specific marker; register JSON providers against this
- `IBinarySerializer : ISerializer` — marker interface for binary implementations
- `SerializerExtensions` — C# 14 extension members on `ISerializer`:
  - `Deserialize<T>(byte[])` — deserialize from a byte array
  - `Deserialize<T>(string?)` — deserialize from a string (UTF-8 for text, Base64 for binary)
  - `SerializeToBytes<T>(T?)` — serialize to `byte[]`, null-safe
  - `SerializeToString<T>(T?)` — serialize to string (UTF-8 for text, Base64 for binary), null-safe

### Installation

```bash
dotnet add package Headless.Serializer.Abstractions
```

### Quick Start

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

### Configuration

None. Abstractions-only package.

### Dependencies

None.

### Side Effects

None.

---
## Headless.Serializer.Json

System.Text.Json implementation of `IJsonSerializer` with opinionated defaults and a rich built-in converter library.

### Problem Solved

Provides JSON serialization via System.Text.Json with a battle-tested default configuration (camelCase, enum strings, cycle-safe, nullable-aware) and a set of reusable converters for common edge cases — all wired through the `IJsonSerializer` abstraction.

### Key Features

- `SystemJsonSerializer` — `IJsonSerializer` implementation; accepts an optional `IJsonOptionsProvider`
- `IJsonOptionsProvider` / `DefaultJsonOptionsProvider` — injectable options split into separate serialize and deserialize `JsonSerializerOptions`
- `JsonConstants` — shared, pre-configured option sets:
  - `DefaultWebJsonOptions` — camelCase, case-insensitive read, enum-as-camelCase-string, `IpAddressJsonConverter`, nullable-annotations respected, trailing commas allowed, cycles ignored
  - `DefaultInternalJsonOptions` — strict (no case-insensitive read, no trailing commas, `WhenWritingNull`)
  - `DefaultPrettyJsonOptions` — `DefaultWebJsonOptions` with `WriteIndented = true`
  - `ConfigureWebJsonOptions(JsonSerializerOptions)` / `ConfigureInternalJsonOptions(JsonSerializerOptions)` — apply settings to an existing instance
- Built-in converters (all in namespace `Headless.Serializer.Converters`):
  - `UnixTimeJsonConverter` — `DateTimeOffset` ↔ Unix epoch seconds (reads number or string)
  - `IpAddressJsonConverter` — `IPAddress?` ↔ string (included in default options)
  - `EmptyStringAsNullJsonConverter<T>` — treats empty string as `null` on read and write
  - `StringToGuidJsonConverter` — `Guid` from N/D/B/P/X formats
  - `NullableStringToGuidJsonConverter` — `Guid?` from N/D/B/P/X formats
  - `StringToBooleanJsonConverter` — `bool` from string `"true"`/`"false"`
  - `SingleOrListJsonConverter<TItem>` — accepts a single JSON value or array as `List<TItem?>`
  - `SingleOrHashsetJsonConverter<TItem>` — accepts a single JSON value or array as `HashSet<TItem?>`
  - `ObjectToInferredTypesJsonConverter` — maps `object` properties to inferred CLR primitives
  - `CollectionItemJsonConverter<TDatatype, TConverterType>` — applies a per-item converter to any `IEnumerable`
- Type info modifiers (namespace `Headless.Serializer.Modifiers`):
  - `SystemJsonTypeInfoResolver` — `DefaultJsonTypeInfoResolver` with pluggable `Modifiers` callbacks
  - `JsonSerializerModifiersOptions` — holds the modifier list
  - `JsonPropertiesModifiers<TClass>.CreateIgnorePropertyModifyAction` — exclude a property from serialization
  - `JsonPropertiesModifiers<TClass>.CreateIncludeNonPublicPropertiesModifyAction` — enable non-public setter deserialization
- `ToObjectExtensions.To<T>(this object?, JsonSerializerOptions?)` — extension on `object?` that converts via `Convert.ChangeType`, `TypeDescriptor`, enum parse, or STJ deserialization depending on the input type

### Design Notes

`SystemJsonSerializer` is annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` because it uses reflection-based STJ APIs. This means it is **not AOT-safe** out of the box. The annotation propagates to call sites — you will see trim warnings in NativeAOT or PublishTrimmed builds. For AOT scenarios, write a source-generated `IJsonSerializer` wrapper using `JsonSerializerContext` and register it instead.

`DefaultWebJsonOptions` adds `JsonStringEnumConverter(CamelCase)` and `IpAddressJsonConverter` automatically. Creating a custom `IJsonOptionsProvider` that calls `JsonConstants.ConfigureWebJsonOptions` inherits these converters without duplication.

### Installation

```bash
dotnet add package Headless.Serializer.Json
```

### Quick Start

```csharp
// Minimal registration — uses DefaultWebJsonOptions for both serialize and deserialize:
builder.Services.AddSingleton<IJsonSerializer, SystemJsonSerializer>();

// With a custom options provider:
builder.Services.AddSingleton<IJsonOptionsProvider, MyJsonOptionsProvider>();
builder.Services.AddSingleton<IJsonSerializer, SystemJsonSerializer>();

// Consume via abstraction:
public sealed class ApiClient(IJsonSerializer serializer)
{
    public async Task<T?> GetAsync<T>(HttpClient client, string url, CancellationToken ct)
    {
        await using var stream = await client.GetStreamAsync(url, ct);
        return serializer.Deserialize<T>(stream);
    }
}
```

### Configuration

Implement `IJsonOptionsProvider` to override the default options:

```csharp
public sealed class MyJsonOptionsProvider : IJsonOptionsProvider
{
    // Starts from DefaultWebJsonOptions, then adds a custom converter:
    public JsonSerializerOptions GetSerializeOptions()
    {
        var opts = JsonConstants.CreateWebJsonOptions();
        opts.Converters.Add(new UnixTimeJsonConverter());
        return opts;
    }

    public JsonSerializerOptions GetDeserializeOptions() => JsonConstants.CreateWebJsonOptions();
}
```

Use type info modifiers to exclude a property without touching the model:

```csharp
var modifier = JsonPropertiesModifiers<MyModel>.CreateIgnorePropertyModifyAction(x => x.InternalField);
// Pass via SystemJsonTypeInfoResolver -> JsonSerializerModifiersOptions -> JsonSerializerOptions.TypeInfoResolver
```

### Dependencies

- `Headless.Serializer.Abstractions`

### Side Effects

None. Registration is fully explicit.

---
## Headless.Serializer.MessagePack

MessagePack binary serialization implementation of `IBinarySerializer`.

### Problem Solved

Provides compact binary serialization for high-throughput scenarios (cache entries, internal message envelopes) where JSON text overhead is a bottleneck. Contractless by default — no `[MessagePackObject]` or `[Key]` attributes required.

### Key Features

- `MessagePackSerializer` — `IBinarySerializer` implementation
- Contractless by default: uses `ContractlessStandardResolver`, so plain POCOs serialize without any attributes
- Accepts `MessagePackSerializerOptions` via constructor for compression, custom resolvers, or security settings
- Built-in LZ4 compression available via `WithCompression(MessagePackCompression.Lz4BlockArray)`
- Full `ISerializer` surface: generic `Serialize<T>` / `Deserialize<T>`, non-generic `Serialize(object?)` / `Deserialize(Stream, Type)`

### Installation

```bash
dotnet add package Headless.Serializer.MessagePack
```

### Quick Start

```csharp
// Default: contractless, no compression:
builder.Services.AddSingleton<IBinarySerializer, MessagePackSerializer>();

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

### Configuration

All configuration is passed via `MessagePackSerializerOptions` at construction time:

```csharp
// Switch to attribute-based (non-contractless) mode:
var options = MessagePackSerializerOptions.Standard; // requires [MessagePackObject]/[Key] attributes

// Security: disallow deserialization of arbitrary types via typeless API:
var options = MessagePackSerializerOptions
    .Standard.WithResolver(ContractlessStandardResolver.Instance)
    .WithSecurity(MessagePackSecurity.UntrustedData);
```

### Dependencies

- `Headless.Serializer.Abstractions`
- `MessagePack`

### Side Effects

None. Registration is fully explicit.
