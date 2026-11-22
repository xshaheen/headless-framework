// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;

namespace Tests;

/// <summary>What one pipeline wrote to each sink, as bytes.</summary>
internal sealed class Exported(IReadOnlyDictionary<Sink, byte[]> sinks)
{
    public bool Has(Sink sink) => sinks.ContainsKey(sink);

    public byte[] this[Sink sink] => sinks[sink];

    /// <summary>
    /// Whether <paramref name="value"/> appears in the sink as written, JSON-escaped, or percent-encoded, the forms an
    /// exporter or propagator may give it.
    /// </summary>
    public bool Contains(Sink sink, string value) => ContainsAnyForm(sinks[sink], value);

    public static bool ContainsAnyForm(byte[] bytes, string value)
    {
        string[] forms = [value, value.Replace("@", "\\u0040", StringComparison.Ordinal), Uri.EscapeDataString(value)];

        return forms
            .Distinct(StringComparer.Ordinal)
            .Any(form => bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(form)) >= 0);
    }

    public string Text(Sink sink) => Encoding.UTF8.GetString(sinks[sink]);
}
