// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Headless.Logging.Internal;

/// <summary>
/// A message template parsed once into a composite format and its hole names in order, so re-rendering a redacted
/// entry does not parse the template on every call.
/// </summary>
internal sealed class LogTemplate
{
    // An app that builds templates by interpolation makes a new one per call; past this many the parse is not kept,
    // so the cache never grows with the log volume.
    private const int _MaxCached = 1024;

    private static readonly ConcurrentDictionary<string, LogTemplate?> _Cache = new(StringComparer.Ordinal);
    private static int _cached;

    private LogTemplate(string[] holes, CompositeFormat format)
    {
        Holes = holes;
        Format = format;
    }

    /// <summary>The hole names in template order, each without its alignment and format.</summary>
    public string[] Holes { get; }

    /// <summary>The template with each hole replaced by its index, alignment and format kept.</summary>
    public CompositeFormat Format { get; }

    /// <summary>The parsed template, or <see langword="null"/> when composite formatting cannot render it.</summary>
    public static LogTemplate? Get(string template)
    {
        if (_Cache.TryGetValue(template, out var known))
        {
            return known;
        }

        var parsed = _Parse(template);

        if (Volatile.Read(ref _cached) < _MaxCached && _Cache.TryAdd(template, parsed))
        {
            Interlocked.Increment(ref _cached);
        }

        return parsed;
    }

    private static LogTemplate? _Parse(string template)
    {
        var composite = new StringBuilder(template.Length);
        List<string> holes = [];

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];

            if (c is '{' or '}' && i + 1 < template.Length && template[i + 1] == c)
            {
                // An escaped brace keeps its escape, which composite formatting reads the same way.
                composite.Append(c).Append(c);
                i++;

                continue;
            }

            if (c == '}')
            {
                return null;
            }

            if (c != '{')
            {
                composite.Append(c);

                continue;
            }

            var close = template.IndexOf('}', i + 1, StringComparison.Ordinal);

            if (close < 0)
            {
                return null;
            }

            var body = template.AsSpan(i + 1, close - i - 1);
            var delimiter = body.IndexOfAny(',', ':');

            composite.Append('{').Append(holes.Count.ToString(CultureInfo.InvariantCulture));

            if (delimiter >= 0)
            {
                composite.Append(body[delimiter..]);
            }

            composite.Append('}');
            holes.Add((delimiter < 0 ? body : body[..delimiter]).ToString());
            i = close;
        }

        try
        {
            return new LogTemplate([.. holes], CompositeFormat.Parse(composite.ToString()));
        }
        catch (FormatException)
        {
            // An alignment or format composite formatting rejects: the caller falls back to the raw rendering.
            return null;
        }
    }
}
