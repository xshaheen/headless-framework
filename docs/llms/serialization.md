---
domain: Serialization
packages: Serializer.Abstractions, Serializer.Json, Serializer.MessagePack
---

# Serialization

> Serializer contracts with a System.Text.Json provider for text and a MessagePack provider for binary.

## Orientation

- **`Headless.Serializer.Abstractions`** holds the contracts: `ISerializer`, `ITextSerializer`, `IJsonSerializer`, `IBinarySerializer`, and the `SerializerExtensions` adapters. Domain and application code references only this package.
- **`Headless.Serializer.Json`** is the default provider: `SystemJsonSerializer` implements `IJsonSerializer`. It also ships the `JsonConstants` option presets, `JsonCanonicalizer`, and a set of converters.
- **`Headless.Serializer.MessagePack`** is the binary provider: `MessagePackBinarySerializer` implements `IBinarySerializer`. Use it for compact internal payloads such as cache entries.

The serializer packages register nothing. Register a provider yourself, or let a package that needs one do it: `AddHeadless()` (through `AddHeadlessJsonService()` in `Headless.Api`), the Redis cache, and the Redis, FileSystem, and SSH.NET blob providers each `TryAdd` an `IJsonSerializer` over the registered `IJsonOptionsProvider` (default `DefaultJsonOptionsProvider`). Your own registration wins in either order.

Both providers can coexist. Register `IJsonSerializer` and `IBinarySerializer` separately, and inject the interface each component needs.

## Agent Rules

- Inject `ISerializer`, `IJsonSerializer`, or `IBinarySerializer`. Do not reference `SystemJsonSerializer` or `MessagePackBinarySerializer` in service code, and do not call `System.Text.Json.JsonSerializer` directly, so the format and options stay in one place.
- Default to `Headless.Serializer.Json`. Choose MessagePack only when payload size or throughput matters and every reader is .NET.
- Keep HTTP JSON and stored JSON apart. HTTP uses `JsonConstants.ConfigureWebJsonOptions`. Anything stored uses `JsonConstants.DefaultStorageJsonOptions`. See [Web-API JSON and stored JSON](#web-api-json-and-stored-json).
- To customize stored JSON, build your `IJsonOptionsProvider` from `JsonConstants.CreateStorageJsonOptions()`, not from `CreateWebJsonOptions()`. The web preset reads leniently and camelCases names, so a stored value would follow your HTTP contract instead of round-tripping.
- Never mutate a `JsonConstants.Default*` preset. They are frozen and throw `InvalidOperationException`. Start from the matching `Create*JsonOptions()` factory.
- Do not add `JsonStringEnumConverter` or `IpAddressJsonConverter` to options built from a `JsonConstants` factory or `Configure*` helper. Every preset already has both, and a duplicate converter changes behavior.
- For a wire contract shared with a non-.NET producer (for example snake_case), call `JsonConstants.CreateInternalJsonOptions(JsonNamingPolicy.SnakeCaseLower)`. The naming policy renames property names and enum values together. Do not set `PropertyNamingPolicy` on a copy by hand, because the enum converter keeps the old naming.
- To hash JSON for a fingerprint, idempotency key, or signature, call `JsonCanonicalizer.Hash` (RFC 8785). Never hash `JsonSerializer` output or a hand-sorted `Utf8JsonWriter` copy: their escaping is .NET-specific, so another language cannot reproduce the bytes.
- Keep `MessagePackBinarySerializer` on its default `MessagePackSecurity.UntrustedData`. Pass `untrustedData: false` only for payloads from inside your trust boundary. When you pass your own `MessagePackSerializerOptions`, set `Security` on them: the `untrustedData` switch is ignored.
- To serialize a generated primitive (`IPrimitive<T>`) with MessagePack, set `<PrimitiveMessagePackFormatters>true</PrimitiveMessagePackFormatters>` in the primitive's project. Without it the primitive writes an empty map and reads back uninitialized, with no error.
- Declare a `[JsonExtensionData]` property `{ get; set; }`, never `init`. A source-generated `JsonSerializerContext` whose models carry one must also declare `[JsonSerializable(typeof(object))]` and `[JsonSerializable(typeof(JsonElement))]`. Either mistake builds clean and throws on deserialization.
- `SystemJsonSerializer` is marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. In a trimmed or NativeAOT app, implement `IJsonSerializer` over a source-generated `JsonSerializerContext` and register that instead. Use `JsonStringEnumConverter<TEnum>` per enum there, because `JsonStringEnumConverter` needs dynamic code.

## Core Concepts

### The `ISerializer` contract

`ISerializer` is buffer-first. Writes target an `IBufferWriter<byte>`. Reads take a `ReadOnlyMemory<byte>` (contiguous, such as a `byte[]` or a cache value) or a `ReadOnlySequence<byte>` (possibly multi-segment, such as from a `PipeReader`):

```csharp
using System.Buffers;

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

`SerializerExtensions` adds adapters for the shapes call sites usually hold. When you already hold a contiguous buffer, call the `ReadOnlyMemory<byte>` overload directly.

| Method | Input | Output | Behavior |
| --- | --- | --- | --- |
| `Deserialize<T>(byte[])` | `byte[]` | `T?` | Reads in place, no copy |
| `Deserialize<T>(string?)` | `string?` | `T?` | UTF-8 for a text serializer, Base64-decoded for a binary one. `null` returns `default` without calling the serializer |
| `Deserialize<T>(Stream)`, `Deserialize(Stream, Type)` | `Stream` | `T?` | Reads the stream into a pooled buffer first |
| `SerializeToBytes<T>(T?)` | `T?` | `byte[]?` | Null-safe |
| `SerializeToString<T>(T?)` | `T?` | `string?` | UTF-8 for a text serializer, Base64 for a binary one |
| `Serialize<T>(T, Stream)`, `Serialize(object?, Stream)` | `T` | `Stream` | Serializes through a pooled buffer, then writes to the stream |

### Interface hierarchy

```text
ISerializer
├── ITextSerializer        (marker)
│   └── IJsonSerializer    (marker)
└── IBinarySerializer      (marker)
```

The markers add no members. They decide what `SerializeToString` and `Deserialize<T>(string?)` do: UTF-8 for `ITextSerializer`, Base64 for `IBinarySerializer`.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Serializer.Json` | API payloads, external interop, human-readable storage, logs, configuration. The general default | Payload size or serializer throughput is the bottleneck on a purely internal format | Larger payloads. Reflection-based, so not trim- or AOT-safe as shipped |
| `Headless.Serializer.MessagePack` | Internal cache entries, high-throughput message envelopes, storage where size matters | A browser, third-party API, or non-.NET client reads the bytes, or you need to read stored bytes by eye | Binary and not human-readable. The default contractless resolver uses reflection and needs no attributes |

---

## Headless.Serializer.Abstractions

The serializer contracts and `SerializerExtensions`, for code that must not depend on a format.

### Setup

```bash
dotnet add package Headless.Serializer.Abstractions
```

```csharp
public sealed class DataService(IJsonSerializer serializer)
{
    public T? Load<T>(Stream stream) => serializer.Deserialize<T>(stream);

    public void Save<T>(T data, Stream stream) => serializer.Serialize(data, stream);

    public byte[]? ToBytes<T>(T data) => serializer.SerializeToBytes(data);

    public T? FromBytes<T>(byte[] data) => serializer.Deserialize<T>(data);
}
```

**Verify:** no startup signal, because the package registers nothing. Resolving a component that injects `IJsonSerializer` or `IBinarySerializer` with no provider registered fails with the container's "Unable to resolve service" `InvalidOperationException`. In a unit test, assert that `serializer.Deserialize<T>(serializer.SerializeToBytes(value))` equals `value`.

### Configuration

None.

### Design and runtime behavior

- `byte[]`, `string`, and `Stream` overloads are convenience adapters over the buffer contract. The `Stream` overloads copy through a pooled buffer.
- `Deserialize<T>(string?)` on a binary serializer expects Base64, the form `SerializeToString` produced.

---

## Headless.Serializer.Json

`SystemJsonSerializer` (System.Text.Json) as `IJsonSerializer`, the `JsonConstants` presets, `JsonCanonicalizer`, and JSON converters.

### Setup

```bash
dotnet add package Headless.Serializer.Json
```

```csharp
// Uses DefaultJsonOptionsProvider: DefaultStorageJsonOptions for both writing and reading.
builder.Services.AddSingleton<IJsonSerializer, SystemJsonSerializer>();

// With your own options, in any order relative to the serializer:
builder.Services.AddSingleton<IJsonOptionsProvider, MyJsonOptionsProvider>();
builder.Services.AddSingleton<IJsonSerializer, SystemJsonSerializer>();

public sealed class ApiClient(IJsonSerializer serializer)
{
    public async Task<T?> GetAsync<T>(HttpClient client, string url, CancellationToken ct)
    {
        await using var stream = await client.GetStreamAsync(url, ct);
        return serializer.Deserialize<T>(stream);
    }
}
```

In an app that calls `AddHeadless()` (`Headless.Api.ServiceDefaults`), `IJsonSerializer`, `ITextSerializer`, and `ISerializer` are already registered over `IJsonOptionsProvider`. Register only an `IJsonOptionsProvider` there to change the options.

**Verify:** no startup signal. In a test, resolve `IJsonSerializer` from the built host and assert that `serializer.Deserialize<T>(serializer.SerializeToBytes(value))` equals `value`. To check a custom provider took effect, assert on the JSON text from `serializer.SerializeToString(value)`.

### Configuration

#### Options providers

`IJsonOptionsProvider` returns separate options for writing (`GetSerializeOptions()`) and reading (`GetDeserializeOptions()`). `DefaultJsonOptionsProvider` returns `DefaultStorageJsonOptions` for both, and `SystemJsonSerializer` uses it when constructed without a provider.

```csharp
using System.Text.Json;
using Headless.Serializer.Converters;

public sealed class MyJsonOptionsProvider : IJsonOptionsProvider
{
    // Start from the storage preset so a cache or blob store reads back what it wrote.
    private static readonly JsonSerializerOptions _Options = _Create();

    public JsonSerializerOptions GetSerializeOptions() => _Options;

    public JsonSerializerOptions GetDeserializeOptions() => _Options;

    private static JsonSerializerOptions _Create()
    {
        var opts = JsonConstants.CreateStorageJsonOptions();
        opts.Converters.Add(new UnixTimeJsonConverter());
        opts.MakeReadOnly(populateMissingResolver: true);
        return opts;
    }
}
```

#### Presets

`JsonConstants` exposes four frozen presets. Each `Create*JsonOptions()` factory returns a fresh mutable copy. `ConfigureWebJsonOptions(options)` and `ConfigureInternalJsonOptions(options, namingPolicy)` apply a preset to an instance you already have and return it.

| Preset | Factory | Behavior | Use for |
| --- | --- | --- | --- |
| `DefaultWebJsonOptions` | `CreateWebJsonOptions()` | camelCase property names. String dictionary keys as written. Case-insensitive reads. Numbers read from strings. Unknown members skipped. Duplicate properties refused. `$type` discriminator accepted in any position. `object` read as `JsonElement`. Enums written as member names (`Active`, or a `[JsonStringEnumMemberName]` alias), numbers refused. Nullable annotations and required constructor parameters respected. Trailing commas allowed. A reference cycle throws `JsonException` instead of writing `null` | HTTP requests and responses |
| `DefaultPrettyJsonOptions` | `CreatePrettyJsonOptions()` | The web preset with `WriteIndented = true` | Logs and diagnostics |
| `DefaultInternalJsonOptions` | `CreateInternalJsonOptions(JsonNamingPolicy? namingPolicy = null)` | Built on `JsonSerializerDefaults.Strict`. No case-insensitive reads, no trailing commas, no comments. Unknown members and duplicate properties refused. Nulls written, because every constructor parameter is required. `object` read as `JsonElement`. `$type` accepted in any position. Enums camelCase, numbers refused | A contract both sides change together, such as a payload shared with another service |
| `DefaultStorageJsonOptions` | `CreateStorageJsonOptions()` | The internal preset with unknown members skipped, plus `ObjectToInferredTypesJsonConverter` | Everything stored or read by a consumer deployed on its own schedule |

The internal preset's `namingPolicy` (for example `JsonNamingPolicy.SnakeCaseLower`) renames property names and enum values. `null` keeps CLR property names and camelCase enum values. String dictionary keys are never renamed. Enum dictionary keys follow the enum naming: `Dictionary<Status, int>` writes `{"pending_payment":1}` under snake_case.

A snake_case contract with a non-.NET producer, optionally over a source-generated context:

```csharp
using System.Text.Json;

public static class OrderWireJson
{
    public static JsonSerializerOptions Options { get; } = _Create();

    private static JsonSerializerOptions _Create()
    {
        var options = JsonConstants.ConfigureInternalJsonOptions(
            new JsonSerializerOptions { TypeInfoResolver = OrderJsonContext.Default },
            JsonNamingPolicy.SnakeCaseLower
        );
        options.MakeReadOnly();

        return options;
    }
}
```

A `[JsonExtensionData]` member absorbs unknown properties, so the internal preset's refusal of unknown members never fires on a type that has one.

#### Converters

The converters live in namespace `Headless.Serializer.Converters`. Add them to your options when needed.

| Converter | Behavior |
| --- | --- |
| `UnixTimeJsonConverter` | `DateTimeOffset` as Unix epoch seconds. Reads a number or a string |
| `IpAddressJsonConverter` | `IPAddress?` as a string. Already in every preset |
| `EmptyStringAsNullJsonConverter<T>` | Treats an empty string as `null` on read and write |
| `StringToGuidJsonConverter`, `NullableStringToGuidJsonConverter` | `Guid` and `Guid?` from the N, D, B, P, and X formats |
| `StringToBooleanJsonConverter` | `bool` from the strings `"true"` and `"false"` |
| `SingleOrListJsonConverter<TItem>`, `SingleOrHashsetJsonConverter<TItem>` | Accept a single value or an array, as `List<TItem?>` or `HashSet<TItem?>` |
| `ObjectToInferredTypesJsonConverter` | Reads `object` as `bool`, `long` for an integer that fits, `decimal` for other numbers (`double` when `decimal` cannot hold the value), `string` for every string, and `JsonElement` for objects and arrays. It never turns a string into a date; parse a date string yourself |
| `CollectionItemJsonConverter<TDatatype, TConverterType>` | Applies a per-item converter to any `IEnumerable` |

#### Type info modifiers

To exclude a property without touching the model, add a modifier from namespace `Headless.Serializer.Modifiers` to a `SystemJsonTypeInfoResolver` and set it as the options' `TypeInfoResolver`:

```csharp
using Headless.Serializer.Modifiers;

var modifiers = new JsonSerializerModifiersOptions();
modifiers.Modifiers.Add(JsonPropertiesModifiers<MyModel>.CreateIgnorePropertyModifyAction(x => x.InternalField));

var options = JsonConstants.CreateStorageJsonOptions();
options.TypeInfoResolver = new SystemJsonTypeInfoResolver(modifiers);
```

`JsonPropertiesModifiers<TClass>.CreateIncludeNonPublicPropertiesModifyAction` lets deserialization set a property with a non-public setter.

### Design and runtime behavior

#### Web-API JSON and stored JSON

The framework uses one preset for HTTP and another for everything stored.

| JSON | Options | Who sets them | Used by |
| --- | --- | --- | --- |
| HTTP requests and responses | `JsonConstants.ConfigureWebJsonOptions`, applied to MVC and minimal-API `JsonOptions` | `AddHeadless()`, `Headless.Api.Mvc`, and `Headless.Api.MinimalApi` | Controllers, minimal APIs, ProblemDetails |
| App types the framework stores | The registered `IJsonOptionsProvider` behind the shared `IJsonSerializer`. Default `DefaultJsonOptionsProvider`, which returns `DefaultStorageJsonOptions` | The host, by registering an `IJsonOptionsProvider` | Redis and hybrid cache values, blob metadata on Redis, FileSystem, and SSH.NET, and any `IJsonSerializer` the app injects |
| Framework-owned records | Fixed per feature | Nobody: the format is a storage contract | `DefaultStorageJsonOptions`: Settings, Features, and Permissions definition `ExtraProperties`, typed setting and feature values (`GetAsync<T>`, `SetAsync<T>`), JSON blobs (`UploadContentAsync`, `GetBlobContentAsync<T>`), EF Core JSON columns, and the default `MessagingOptions.JsonSerializerOptions` for message payloads. `DefaultInternalJsonOptions`: audit-log `OldValues`, `NewValues`, and `ChangedFields` on every provider. Coordination node metadata uses `DefaultJsonOptionsProvider` |

Two rules follow:

- Never store with the HTTP preset. It reads leniently and names members for a client, so another node reading the value back can see something different.
- A framework-owned record ignores your `IJsonOptionsProvider`. Every node and every storage provider writes the same JSON for the same value, so a stored definition compares equal to the declared one and an audit entry reads back the same on every provider.

A stored `object` value comes back in the shape JSON can express, not as the CLR type written: `string`, `bool`, `long`, `decimal`, or `JsonElement`. Read it with `GetProperty<T>(name)` on an `ExtraProperties` owner, or with the `JsonElement` API. To compare a value built in code with a stored one, pass it through `options.RoundTrip(value)` first (`JsonOptionsExtensions.RoundTrip<T>`, namespace `Headless.Serializer`): under the storage preset an `int` in an `object` slot reads back as `long` and an enum as its name. The definition seeders do this, so re-seeding unchanged definitions writes nothing and `seed verify` reports the store current.

The storage preset skips unknown members, so during a rolling deploy an older node still reads what a newer node wrote. If that older node reads a value and writes it back, the new member is dropped. The internal preset refuses unknown members, so a contract both sides change together fails loudly instead.

Every preset refuses duplicate properties, because parsers disagree on which copy wins and a proxy could vet a different payload than the app binds. Every preset accepts the `$type` discriminator after ordinary properties, because PostgreSQL `jsonb` reorders keys.

The web preset writes string dictionary keys as given, because they are data, not member names. A response that wants camelCase keys names them itself: validation errors key each field by its camelCase path, and `ErrorDescriptor.Params` stores its keys camelCase (`minLength`). The web preset writes enums under their member names, so a value a client copies from a response or the OpenAPI document binds back in a route, query, or header without a rewrite. A reference cycle in a response throws `JsonException`; return a DTO, not an entity graph.

#### `SystemJsonSerializer`

- The buffer path honors the options' `WriteIndented`, `Encoder`, `IndentCharacter`, `IndentSize`, `NewLine`, and `MaxDepth` on write, and `AllowTrailingCommas`, `ReadCommentHandling`, and `MaxDepth` on a `ReadOnlySequence<byte>` read.
- Every read path rejects non-whitespace after the top-level value, so a corrupt `{...}<garbage>` payload throws `JsonException` instead of deserializing.
- `Serialize(object? value, ...)` with `null` writes the JSON literal `null`.

#### `JsonCanonicalizer`

`JsonCanonicalizer` writes RFC 8785 (JSON Canonicalization Scheme) output and its SHA-256 hash, so another language can reproduce the same value. Hash the JSON as sent or stored. Serializing an object first makes the hash depend on the naming policy and converters.

```csharp
var key = Convert.ToHexStringLower(JsonCanonicalizer.Hash(requestBodyUtf8));
```

- `Canonicalize(JsonElement, IBufferWriter<byte>)` and `Canonicalize(ReadOnlySpan<byte>, IBufferWriter<byte>)` write the canonical UTF-8. The single-argument overloads return it as `byte[]`.
- `Hash(JsonElement)` and `Hash(ReadOnlySpan<byte>)` return the 32-byte SHA-256 (`HashSizeInBytes`). The overloads that take a `Span<byte> destination` write it there instead.

| Input | Behavior |
| --- | --- |
| Object members | Sorted ordinally by UTF-16 code units |
| Whitespace | Removed |
| Strings | Only `"`, `\`, and U+0000 to U+001F are escaped (`\b \t \n \f \r`, otherwise lowercase `\u00xx`). Everything else, including non-ASCII, `<`, `+`, and emoji, is literal UTF-8 |
| Numbers | ECMAScript `Number.prototype.toString` of the IEEE-754 double: `1.0` gives `1`, `1e21` gives `1e+21`, `-0` gives `0`, `0.10000000000000001` gives `0.1` |
| Integer literal outside ±(2^53 − 1) | `JsonException`, because rounding would give distinct identifiers one hash. Send such values as strings |
| Number outside the double range (`1e400`) | `JsonException` |
| Duplicate member names, lone surrogates | `JsonException` |
| `ReadOnlySpan<byte>` input with comments, trailing commas, or trailing content | `JsonException` |
| `ReadOnlySpan<byte>` input nested deeper than 1,000 levels | `JsonException`. A `JsonElement` keeps the depth limit of its `JsonDocument`, 64 by default |

The number rules match the Python `rfc8785` package. That package takes Python objects, not JSON text, so its result depends on the parser in front of it: Python's `json.loads` keeps the last of two duplicate members, where `JsonCanonicalizer` rejects the document.

#### `To<T>`

`ToObjectExtensions.To<T>(this object?, JsonSerializerOptions? options = null)` (namespace `Headless.Serializer`) converts a value through `Convert.ChangeType`, a `TypeConverter`, an enum parse, or JSON deserialization, depending on the input type.

---

## Headless.Serializer.MessagePack

`MessagePackBinarySerializer`, a MessagePack implementation of `IBinarySerializer`.

### Setup

```bash
dotnet add package Headless.Serializer.MessagePack
```

```csharp
using MessagePack;
using MessagePack.Resolvers;

// Default: contractless resolver, no compression, MessagePackSecurity.UntrustedData.
builder.Services.AddSingleton<IBinarySerializer, MessagePackBinarySerializer>();

// Trusted payloads only, when the trust boundary is explicit:
builder.Services.AddSingleton<IBinarySerializer>(new MessagePackBinarySerializer(untrustedData: false));

// LZ4 compression. These options replace the defaults, so set Security yourself:
var options = MessagePackSerializerOptions
    .Standard.WithResolver(ContractlessStandardResolver.Instance)
    .WithSecurity(MessagePackSecurity.UntrustedData)
    .WithCompression(MessagePackCompression.Lz4BlockArray);

builder.Services.AddSingleton<IBinarySerializer>(new MessagePackBinarySerializer(options));

public sealed class CacheWriter(IBinarySerializer serializer)
{
    public byte[]? Serialize<T>(T value) => serializer.SerializeToBytes(value);

    public T? Deserialize<T>(byte[] data) => serializer.Deserialize<T>(data);
}
```

**Verify:** no startup signal. In a test, assert that `serializer.Deserialize<T>(serializer.SerializeToBytes(value))` equals `value` for each type you store, including every generated primitive. A primitive without `PrimitiveMessagePackFormatters` fails that assertion because it reads back uninitialized.

### Configuration

The constructor is `MessagePackBinarySerializer(MessagePackSerializerOptions? options = null, bool untrustedData = true)`.

- With no `options`, it uses `MessagePackSerializerOptions.Standard` with `ContractlessStandardResolver`, which maps properties by name with no attributes, and applies `MessagePackSecurity.UntrustedData` unless `untrustedData` is `false`.
- With `options`, it uses them as given. `untrustedData` is ignored, and the options' `Security` decides the level.
- For attribute-based contracts (`[MessagePackObject]`, `[Key]`), pass `MessagePackSerializerOptions.Standard` with the resolver you need.

### Design and runtime behavior

- `MessagePackSecurity.UntrustedData` limits recursion depth and uses collision-resistant hashing, so the default is safe for cross-service caches and external producers.
- Generated primitives (`IPrimitive<T>`) round-trip only with `<PrimitiveMessagePackFormatters>true</PrimitiveMessagePackFormatters>` in their project. The generated formatter writes the underlying value and reads it back through the validating constructor, so an invalid payload fails the read. The contractless resolver picks the formatter up with nothing to compose. See [Headless.Generator.Primitives](utilities.md#headlessgeneratorprimitives).
- `SerializeToString` returns Base64, and `Deserialize<T>(string?)` expects Base64.
