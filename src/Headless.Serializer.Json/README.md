# Headless.Serializer.Json

System.Text.Json implementation of `IJsonSerializer` with opinionated defaults and a rich built-in converter library.

## Problem Solved

Provides JSON serialization via System.Text.Json with a battle-tested default configuration (camelCase, enum strings, cycle-safe, nullable-aware) and a set of reusable converters for common edge cases — all wired through the `IJsonSerializer` abstraction.

## Key Features

- `SystemJsonSerializer` — `IJsonSerializer` implementation; accepts an optional `IJsonOptionsProvider`
- `IJsonOptionsProvider` / `DefaultJsonOptionsProvider` — injectable options split into separate serialize and deserialize `JsonSerializerOptions`
- `JsonConstants` — shared, pre-configured option sets:
  - `DefaultWebJsonOptions` — camelCase, case-insensitive read, enum-as-camelCase-string, `IpAddressJsonConverter`, nullable-annotations respected, trailing commas allowed, cycles ignored
  - `DefaultInternalJsonOptions` — strict (no case-insensitive read, no trailing commas, `WhenWritingNull`)
  - `DefaultPrettyJsonOptions` — `DefaultWebJsonOptions` with `WriteIndented = true`
  - `ConfigureWebJsonOptions(JsonSerializerOptions)` / `ConfigureInternalJsonOptions(JsonSerializerOptions)` — apply settings to an existing instance
- Built-in converters (namespace `Headless.Serializer.Converters`):
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

## Design Notes

`SystemJsonSerializer` is annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` because it uses reflection-based STJ APIs. This means it is **not AOT-safe** out of the box. For AOT scenarios, write a source-generated `IJsonSerializer` wrapper using `JsonSerializerContext` and register it instead.

`DefaultWebJsonOptions` adds `JsonStringEnumConverter(CamelCase)` and `IpAddressJsonConverter` automatically. A custom `IJsonOptionsProvider` that calls `JsonConstants.ConfigureWebJsonOptions` inherits these converters without duplication — do not add them a second time.

`SystemJsonSerializer` implements the buffer-first `ISerializer` contract over `System.Text.Json`'s lowest-allocation surface, with no intermediate `byte[]` or `Stream`. Writes go through a `Utf8JsonWriter` constructed over the caller's `IBufferWriter<byte>`; reads use `JsonSerializer.Deserialize(span)` for the contiguous `ReadOnlyMemory<byte>` path and a `Utf8JsonReader` for the `ReadOnlySequence<byte>` path. A pre-built `Utf8JsonWriter`/`Utf8JsonReader` governs its **own** formatting and reading rules independently of the `JsonSerializerOptions`, so the serializer derives them from the options — the writer inherits `WriteIndented`, `Encoder`, `IndentCharacter`/`IndentSize`, `NewLine`, and `MaxDepth`; the sequence reader inherits `AllowTrailingCommas`, `ReadCommentHandling`, and `MaxDepth`. Without that copy, indentation/escaping and the configured depth limit would be silently dropped on the buffer path. The sequence/`Stream` path also rejects trailing non-whitespace after the top-level value, matching the contiguous span/`byte[]` path (which `System.Text.Json` rejects natively) so a corrupt `"{...}<garbage>"` payload cannot deserialize silently. `byte[]`, `string`, and `Stream` remain available as `SerializerExtensions` adapters.

## Installation

```bash
dotnet add package Headless.Serializer.Json
```

## Quick Start

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

## Configuration

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

## Dependencies

- `Headless.Serializer.Abstractions`

## Side Effects

None. Registration is fully explicit.
