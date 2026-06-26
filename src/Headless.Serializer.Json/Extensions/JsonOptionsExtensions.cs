// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>
/// Extension members that project a <see cref="JsonSerializerOptions"/> onto the low-level
/// <see cref="JsonWriterOptions"/> / <see cref="JsonReaderOptions"/> needed to build a pre-made
/// <see cref="System.Text.Json.Utf8JsonWriter"/> or <see cref="System.Text.Json.Utf8JsonReader"/> that stays
/// consistent with the serializer options.
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
}
