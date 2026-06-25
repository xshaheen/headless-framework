// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Serializer;

/// <summary>
/// <see cref="IJsonSerializer"/> implementation backed by <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
/// <remarks>
/// Serialization and deserialization options are provided by the injected <see cref="IJsonOptionsProvider"/>.
/// When no provider is supplied the <see cref="DefaultJsonOptionsProvider"/> is used, which delegates to
/// <see cref="JsonConstants.DefaultWebJsonOptions"/>.
/// <para>
/// Writes go through a <see cref="Utf8JsonWriter"/> over the caller's <see cref="IBufferWriter{T}"/> and reads
/// through <see cref="System.Text.Json.JsonSerializer"/>'s span/reader overloads, so no intermediate
/// <c>byte[]</c> or <see cref="Stream"/> is materialized on the hot path.
/// </para>
/// <para>
/// This class is annotated with <see cref="RequiresUnreferencedCodeAttribute"/> and
/// <see cref="RequiresDynamicCodeAttribute"/> because the reflection-based <c>System.Text.Json</c> path is
/// used internally. AOT-compatible scenarios should instead register a source-generated
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> via a custom
/// <see cref="IJsonOptionsProvider"/>.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SystemJsonSerializer(IJsonOptionsProvider? optionsProvider = null) : IJsonSerializer
{
    private readonly IJsonOptionsProvider _optionsProvider = optionsProvider ?? new DefaultJsonOptionsProvider();

    public void Serialize<T>(T value, IBufferWriter<byte> output)
    {
        var options = _optionsProvider.GetSerializeOptions();
        using var writer = new Utf8JsonWriter(output, options.ToJsonWriterOptions());
        JsonSerializer.Serialize(writer, value, options);
    }

    public void Serialize(object? value, IBufferWriter<byte> output)
    {
        var options = _optionsProvider.GetSerializeOptions();
        using var writer = new Utf8JsonWriter(output, options.ToJsonWriterOptions());
        // Mirror the non-generic contract: a null value encodes the JSON literal `null` as `typeof(object)`.
        JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        return JsonSerializer.Deserialize<T>(data.Span, _optionsProvider.GetDeserializeOptions());
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> data)
    {
        var options = _optionsProvider.GetDeserializeOptions();
        var reader = new Utf8JsonReader(data, options.ToJsonReaderOptions());
        var result = JsonSerializer.Deserialize<T>(ref reader, options);
        _ThrowIfTrailingContent(ref reader);

        return result;
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        return JsonSerializer.Deserialize(data.Span, type, _optionsProvider.GetDeserializeOptions());
    }

    public object? Deserialize(in ReadOnlySequence<byte> data, Type type)
    {
        var options = _optionsProvider.GetDeserializeOptions();
        var reader = new Utf8JsonReader(data, options.ToJsonReaderOptions());
        var result = JsonSerializer.Deserialize(ref reader, type, options);
        _ThrowIfTrailingContent(ref reader);

        return result;
    }

    // JsonSerializer.Deserialize(ref reader) reads a single value and stops; unlike the span/byte[] overloads it
    // does not reject trailing non-whitespace. Mirror those overloads — the whole input is exactly one value — so
    // the sequence/Stream path cannot silently accept a corrupt "{...}<garbage>" payload. Trailing whitespace is
    // skipped by the reader and leaves nothing to read.
    private static void _ThrowIfTrailingContent(ref Utf8JsonReader reader)
    {
        if (reader.Read())
        {
            throw new JsonException("The input contains trailing content after the top-level JSON value.");
        }
    }
}
