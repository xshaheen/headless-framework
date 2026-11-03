// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Serializer;

/// <summary>
/// Extension members on <see cref="JsonSerializerOptions"/>: projections onto the low-level
/// <see cref="JsonWriterOptions"/> / <see cref="JsonReaderOptions"/> needed to build a pre-made
/// <see cref="System.Text.Json.Utf8JsonWriter"/> or <see cref="System.Text.Json.Utf8JsonReader"/> that stays
/// consistent with the serializer options, and <see cref="RoundTrip{T}"/>, which shows a value as these options read
/// it back.
/// </summary>
[PublicAPI]
public static class JsonOptionsExtensions
{
    /// <summary>Projects the formatting and limit settings of <paramref name="options"/> onto a <see cref="JsonWriterOptions"/>.</summary>
    /// <remarks>
    /// A pre-made <see cref="System.Text.Json.Utf8JsonWriter"/> governs its OWN formatting and limits (indentation,
    /// encoder, depth) independently of the <see cref="JsonSerializerOptions"/> passed to
    /// <see cref="System.Text.Json.JsonSerializer"/>'s writer overloads — so the writer must inherit these settings or
    /// the configured escaping/indentation/depth limit is silently ignored, keeping the output byte-identical to
    /// <c>SerializeToUtf8Bytes</c>. Validation is left on (the default) so a buggy custom converter that emits a
    /// malformed token sequence is caught rather than producing structurally invalid JSON.
    /// </remarks>
    /// <param name="options">The serializer options to project.</param>
    /// <returns>A <see cref="JsonWriterOptions"/> mirroring the formatting and depth settings of <paramref name="options"/>.</returns>
    [Pure]
    public static JsonWriterOptions ToJsonWriterOptions(this JsonSerializerOptions options)
    {
        return new JsonWriterOptions
        {
            Encoder = options.Encoder,
            Indented = options.WriteIndented,
            IndentCharacter = options.IndentCharacter,
            IndentSize = options.IndentSize,
            NewLine = options.NewLine,
            MaxDepth = options.MaxDepth,
        };
    }

    /// <summary>Projects the reading rules of <paramref name="options"/> onto a <see cref="JsonReaderOptions"/>.</summary>
    /// <remarks>
    /// A <see cref="System.Text.Json.Utf8JsonReader"/> built from a sequence governs its OWN reading rules (trailing
    /// commas, comment handling, max depth) independently of the <see cref="JsonSerializerOptions"/> passed to
    /// <see cref="System.Text.Json.JsonSerializer"/>'s reader overloads — so the reader must inherit them or the
    /// sequence/Stream paths silently reject payloads the span path (which derives the reader internally) accepts.
    /// </remarks>
    /// <param name="options">The serializer options to project.</param>
    /// <returns>A <see cref="JsonReaderOptions"/> mirroring the reading rules of <paramref name="options"/>.</returns>
    [Pure]
    public static JsonReaderOptions ToJsonReaderOptions(this JsonSerializerOptions options)
    {
        return new JsonReaderOptions
        {
            AllowTrailingCommas = options.AllowTrailingCommas,
            CommentHandling = options.ReadCommentHandling,
            MaxDepth = options.MaxDepth,
        };
    }

    /// <summary>Returns <paramref name="value"/> as it reads back after being written with <paramref name="options"/>.</summary>
    /// <remarks>
    /// Compare a value built in code with one read from storage through this, not directly: a stored value can read
    /// back as a different CLR value than the one written. With <see cref="JsonConstants.DefaultStorageJsonOptions"/>
    /// an <see langword="object"/> holding an <see cref="int"/> reads back as a <see cref="long"/>, an enum as its name,
    /// and an object or array as a <see cref="JsonElement"/>.
    /// </remarks>
    /// <param name="options">The options the value is stored with.</param>
    /// <param name="value">The value to write and read back.</param>
    /// <typeparam name="T">The type the value is stored and read as.</typeparam>
    /// <returns>The value read back, or <see langword="null"/> when <paramref name="value"/> writes as JSON <see langword="null"/>.</returns>
    [Pure]
    [RequiresUnreferencedCode(
        "JSON serialization and deserialization might require types that cannot be statically analyzed."
    )]
    [RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
    public static T? RoundTrip<T>(this JsonSerializerOptions options, T value)
    {
        Argument.IsNotNull(options);

        var json = JsonSerializer.SerializeToUtf8Bytes(value, options);

        return JsonSerializer.Deserialize<T>(json, options);
    }
}
