// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;

namespace Headless.Messaging.Persistence;

/// <summary>
/// The text one header takes inside a stored envelope, so a store that keeps the envelope as text can find a header
/// value without a column of its own.
/// </summary>
internal static class StoredEnvelopeHeader
{
    /// <summary>
    /// Returns <paramref name="name"/> and <paramref name="value"/> as <paramref name="options"/> write them inside the
    /// envelope's header object: <c>"name":"value"</c>, with the options' key policy, escaping, and spacing.
    /// </summary>
    /// <remarks>
    /// The quotes bound the value, so the text matches that value exactly and never a longer one. A quote inside a JSON
    /// string is escaped, so no string in a body can imitate it; only an object property of the same name and value can.
    /// </remarks>
    public static string Text(JsonSerializerOptions options, string name, string value)
    {
        // The envelope's headers are an IDictionary<string, string?>, so its options resolve this type.
        var typeInfo =
            (JsonTypeInfo<IDictionary<string, string?>>)options.GetTypeInfo(typeof(IDictionary<string, string?>));
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = value },
            typeInfo
        );

        // Strip the braces and any indentation around the one pair; the pair's own spacing is the envelope's.
        return json.AsSpan(1, json.Length - 2).Trim().ToString();
    }
}
