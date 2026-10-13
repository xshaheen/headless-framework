// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace Headless.Messaging;

/// <summary>
/// A message body carried as the UTF-8 JSON its producer wrote, for a consumer that parses the body itself.
/// </summary>
/// <remarks>
/// <para>
/// Declare a contract as <c>Message&lt;RawJson&gt;(name)</c> and consume it with <c>IConsume&lt;RawJson&gt;</c> when the
/// producer's JSON does not fit the host's <c>MessagingOptions.JsonSerializerOptions</c>, such as a non-.NET producer
/// on a <c>RawPayload()</c> lane that writes snake_case or a schema its own parser enforces. The serializer neither
/// binds nor rewrites the body: <see cref="Utf8Json"/> holds the producer's bytes, escaping and whitespace included,
/// on the first delivery and on every retry from storage.
/// </para>
/// <para>
/// The body must still be one complete JSON value. A body that is not JSON fails deserialization and takes the poison
/// path without a retry, and a JSON <see langword="null"/> body is an empty message. Publishing a <see cref="RawJson"/> sends
/// <see cref="Utf8Json"/> unchanged.
/// </para>
/// </remarks>
[PublicAPI]
[JsonConverter(typeof(RawJsonConverter))]
public sealed class RawJson
{
    /// <summary>Creates a raw JSON body from a copy of <paramref name="utf8Json"/>.</summary>
    /// <param name="utf8Json">One complete UTF-8 encoded JSON value.</param>
    /// <exception cref="ArgumentException"><paramref name="utf8Json"/> is empty, is not valid JSON, or holds more than one JSON value.</exception>
    public RawJson(ReadOnlyMemory<byte> utf8Json)
    {
        _Validate(utf8Json.Span);
        Utf8Json = utf8Json.ToArray();
    }

    // The converter hands over a fresh array the reader has already validated.
    private RawJson(byte[] validatedUtf8Json)
    {
        Utf8Json = validatedUtf8Json;
    }

    /// <summary>Gets the UTF-8 encoded JSON value exactly as it was produced.</summary>
    public ReadOnlyMemory<byte> Utf8Json { get; }

    /// <summary>Returns the JSON text.</summary>
    public override string ToString() => Encoding.UTF8.GetString(Utf8Json.Span);

    private static void _Validate(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            reader.Read();
            reader.Skip();

            // Reading past the value is the trailing-content check: a single-value reader throws on anything but the end.
            _ = reader.Read();
        }
        catch (JsonException e)
        {
            throw new ArgumentException("The input is not one complete UTF-8 JSON value.", nameof(utf8Json), e);
        }
    }

    internal sealed class RawJsonConverter : JsonConverter<RawJson>
    {
        public override RawJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // The raw value keeps the producer's escaping and whitespace, which re-serializing the element would not.
            using var document = JsonDocument.ParseValue(ref reader);

            return new RawJson(JsonMarshal.GetRawUtf8Value(document.RootElement).ToArray());
        }

        public override void Write(Utf8JsonWriter writer, RawJson value, JsonSerializerOptions options)
        {
            writer.WriteRawValue(value.Utf8Json.Span, skipInputValidation: true);
        }
    }
}
