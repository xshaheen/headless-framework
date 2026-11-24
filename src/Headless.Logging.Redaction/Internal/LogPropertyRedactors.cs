// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.Compliance.Redaction;

namespace Headless.Logging.Internal;

/// <summary>
/// The redactor for each property name the app classified with <c>ClassifyProperty</c>, matched whole and ignoring
/// case. One instance per container, so logs, enrichers, span tags, and baggage redact a name the same way.
/// </summary>
internal sealed class LogPropertyRedactors(FrozenDictionary<string, Redactor> redactors)
{
    public static readonly LogPropertyRedactors Empty = new(FrozenDictionary<string, Redactor>.Empty);

    /// <summary>The text logging renders for a null value, inside a collection or in a template hole.</summary>
    public const string NullText = "(null)";

    public FrozenDictionary<string, Redactor> Redactors { get; } = redactors;

    public int Count => Redactors.Count;

    public bool TryGetRedactor(
        string name,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Redactor? redactor
    ) => Redactors.TryGetValue(name, out redactor);

    /// <summary>
    /// Redacts a value found under a classified name: a collection element by element, anything else, a string or
    /// buffer included, as one value, so the result is a string or a string array whatever the value's type.
    /// </summary>
    public static object Redact(Redactor redactor, object value)
    {
        if (value is IEnumerable items && !IsSingleValue(value))
        {
            List<string> redacted = [];

            foreach (var item in items)
            {
                redacted.Add(item is null ? NullText : _RedactScalar(redactor, item));
            }

            return redacted.ToArray();
        }

        return _RedactScalar(redactor, value);
    }

    /// <summary>
    /// Whether an enumerable value is one value: redacting a string or buffer element by element would give each
    /// character or byte its own token.
    /// </summary>
    public static bool IsSingleValue(object value) => value is string or char[] or byte[];

    private static string _RedactScalar(Redactor redactor, object value) =>
        value switch
        {
            char[] chars => redactor.Redact(new string(chars)),
            byte[] bytes => redactor.Redact(Convert.ToBase64String(bytes)),
            // Invariant culture keeps a pseudonym the same on every host.
            _ => redactor.Redact(value, format: null, CultureInfo.InvariantCulture),
        };
}
