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
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Serializer.Json](#headlessserializerjson)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Serializer.MessagePack](#headlessserializermessagepack)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
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
- Register JSON: `services.AddSingleton<IJsonSerializer, SystemJsonSerializer>()`. Also register a custom `IJsonOptionsProvider` if you need non-default options (registration order does not matter — it is resolved by constructor injection).
- Register MessagePack: `services.AddSingleton<IBinarySerializer, MessagePackSerializer>()`. Pass custom `MessagePackSerializerOptions` via constructor for compression or resolver changes.
- **MessagePack security**: the parameterless `MessagePackSerializer()` uses `MessagePackSecurity.UntrustedData`, so it is safe for cross-service caches and external producers by default. Use `new MessagePackSerializer(untrustedData: false)` only when payloads originate inside the current trust boundary and the MessagePack-CSharp fast path is a deliberate choice. When you supply your own `MessagePackSerializerOptions`, set `Security` there — the `untrustedData` switch is ignored and never changes the level you chose.
- `SystemJsonSerializer` is annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` — not AOT-safe as-is. For AOT/NativeAOT scenarios, implement a source-generated `IJsonSerializer` instead.
- The `ISerializer` contract is buffer-first: writes target an `IBufferWriter<byte>`, reads consume a `ReadOnlyMemory<byte>` or `ReadOnlySequence<byte>`. Use extension methods (`SerializeToBytes<T>`, `SerializeToString<T>`, `Deserialize<T>(byte[])`, `Deserialize<T>(string?)`, plus `Serialize<T>(T, Stream)` / `Deserialize<T>(Stream)`) from `SerializerExtensions` when you hold a `byte[]`, `string`, or `Stream` instead.
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
    void Serialize<T>(T value, IBufferWriter<byte> output);
    void Serialize(object? value, IBufferWriter<byte> output);
    T? Deserialize<T>(ReadOnlyMemory<byte> data);
    T? Deserialize<T>(in ReadOnlySequence<byte> data);
    object? Deserialize(ReadOnlyMemory<byte> data, Type type);
    object? Deserialize(in ReadOnlySequence<byte> data, Type type);
}
```

The base contract is buffer-first: writes target an `IBufferWriter<byte>`; reads consume a `ReadOnlyMemory<byte>` (contiguous — the common case: a `byte[]`, a cache value segment) or a `ReadOnlySequence<byte>` (possibly multi-segment, e.g. from a `PipeReader`). This lets each implementation reach its backend's lowest-allocation path — `System.Text.Json`'s `Utf8JsonWriter`/`Utf8JsonReader`, MessagePack's `IBufferWriter`/`ReadOnlySequence` APIs — with no intermediate `byte[]` or `Stream`. Extension methods in `SerializerExtensions` (C# 14 extension members) add adapters for the shapes consumers usually hold:

| Method | Input | Output | Note |
|--------|-------|--------|------|
| `Deserialize<T>(byte[])` | `byte[]` | `T?` | wraps as `ReadOnlyMemory<byte>` — read in place, no copy |
| `Deserialize<T>(string?)` | `string?` | `T?` | UTF-8 for text; Base64-decode for binary; `null` → `default` (serializer not invoked) |
| `Deserialize<T>(Stream)` | `Stream` | `T?` | reads the stream into a pooled buffer first |
| `SerializeToBytes<T>(T?)` | `T?` | `byte[]?` | null-safe; serializes through a pooled buffer writer |
| `SerializeToString<T>(T?)` | `T?` | `string?` | UTF-8 for text; Base64 for binary |
| `Serialize<T>(T, Stream)` | `T` | `Stream` | serializes through a pooled buffer, then writes to the stream |

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

### Design Notes

The contract is **buffer-first, not Stream-first**. `IBufferWriter<byte>` and `ReadOnlyMemory<byte>` / `ReadOnlySequence<byte>` are the primitives both backends expose with the fewest copies (`System.Text.Json` via `Utf8JsonWriter`/`Utf8JsonReader`, MessagePack natively) — so a `SerializeToBytes` no longer pays for a `MemoryStream` plus its `ToArray()` copy, and a byte-array deserialize is read in place. `byte[]`, `string`, and `Stream` are provided as extension adapters because they are convenient at call sites, not because they are the fast path. When you already hold a contiguous buffer (a `byte[]`, a cache value segment), call the `ReadOnlyMemory<byte>` overload directly to avoid the adapter hop.

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
- `JsonConstants` — shared, pre-configured, **read-only** (frozen) option sets. Customize by copying via a `Create*JsonOptions()` factory rather than mutating a preset:
  - `DefaultWebJsonOptions` — camelCase, case-insensitive read, enum-as-camelCase-string, `IpAddressJsonConverter`, nullable-annotations respected, trailing commas allowed, cycles ignored
  - `DefaultInternalJsonOptions` — strict (no case-insensitive read, no trailing commas, `WhenWritingNull`)
  - `DefaultPrettyJsonOptions` — `DefaultWebJsonOptions` with `WriteIndented = true`
  - `CreateWebJsonOptions()` / `CreateInternalJsonOptions()` / `CreatePrettyJsonOptions()` — return a fresh **mutable** instance with the matching preset applied
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
- `ToObjectExtensions.To<T>(this object?, JsonSerializerOptions?)` (namespace `Headless.Serializer`) — extension on `object?` that converts via `Convert.ChangeType`, `TypeDescriptor`, enum parse, or STJ deserialization depending on the input type. Requires `using Headless.Serializer;`

### Design Notes

`SystemJsonSerializer` is annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` because it uses reflection-based STJ APIs. This means it is **not AOT-safe** out of the box. The annotation propagates to call sites — you will see trim warnings in NativeAOT or PublishTrimmed builds. For AOT scenarios, write a source-generated `IJsonSerializer` wrapper using `JsonSerializerContext` and register it instead.

The three `JsonConstants` presets (`DefaultWebJsonOptions`, `DefaultInternalJsonOptions`, `DefaultPrettyJsonOptions`) are frozen (`JsonSerializerOptions.IsReadOnly == true`) at initialization so they can be shared process-wide without a caller silently reconfiguring framework serialization. Mutating a preset throws `InvalidOperationException` — start from a fresh mutable copy via `CreateWebJsonOptions()` / `CreateInternalJsonOptions()` / `CreatePrettyJsonOptions()` instead.

`DefaultWebJsonOptions` adds `JsonStringEnumConverter(CamelCase)` and `IpAddressJsonConverter` automatically. Creating a custom `IJsonOptionsProvider` that calls `JsonConstants.ConfigureWebJsonOptions` inherits these converters without duplication.

`SystemJsonSerializer` implements the buffer-first `ISerializer` contract over `System.Text.Json`'s lowest-allocation surface, with no intermediate `byte[]` or `Stream`. Writes go through a `Utf8JsonWriter` constructed over the caller's `IBufferWriter<byte>`; reads use `JsonSerializer.Deserialize(span)` for the contiguous `ReadOnlyMemory<byte>` path and a `Utf8JsonReader` for the `ReadOnlySequence<byte>` path. A pre-built `Utf8JsonWriter`/`Utf8JsonReader` governs its **own** formatting and reading rules independently of the `JsonSerializerOptions`, so the serializer derives them from the options — the writer inherits `WriteIndented`, `Encoder`, `IndentCharacter`/`IndentSize`, `NewLine`, and `MaxDepth`; the sequence reader inherits `AllowTrailingCommas`, `ReadCommentHandling`, and `MaxDepth`. Without that copy, indentation/escaping and the configured depth limit would be silently dropped on the buffer path. The sequence/`Stream` path also rejects trailing non-whitespace after the top-level value, matching the contiguous span/`byte[]` path, so a corrupt `"{...}<garbage>"` payload cannot deserialize silently. `byte[]`, `string`, and `Stream` remain available as `SerializerExtensions` adapters.

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
- Applies `MessagePackSecurity.UntrustedData` by default; pass `untrustedData: false` only for trusted payloads where the MessagePack-CSharp fast path is intentional
- Built-in LZ4 compression available via `WithCompression(MessagePackCompression.Lz4BlockArray)`
- Full `ISerializer` surface via MessagePack's native buffer APIs — `Serialize(IBufferWriter<byte>)`, `Deserialize(ReadOnlyMemory<byte>)` / `Deserialize(in ReadOnlySequence<byte>)` — avoiding the buffer-copy overhead of its `Stream` overloads

### Design Notes

The parameterless constructor uses `MessagePackSecurity.UntrustedData` so default deserialization is safe for cross-service caches, external message producers, and other payloads outside the current process trust boundary. For trusted payloads where the MessagePack-CSharp fast path is intentional, construct with `untrustedData: false` or supply custom `MessagePackSerializerOptions` with the desired `Security`. When you pass options, the serializer uses them verbatim and you own the security level.

### Installation

```bash
dotnet add package Headless.Serializer.MessagePack
```

### Quick Start

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

### Configuration

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

### Dependencies

- `Headless.Serializer.Abstractions`
- `MessagePack`

### Side Effects

None. Registration is fully explicit.
