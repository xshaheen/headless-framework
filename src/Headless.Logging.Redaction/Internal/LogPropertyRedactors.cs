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

    public FrozenDictionary<string, Redactor> Redactors { get; } = redactors;

    public int Count => Redactors.Count;

    public bool TryGetRedactor(
        string name,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Redactor? redactor
    ) => Redactors.TryGetValue(name, out redactor);

    /// <summary>
    /// Redacts a value found under a classified name: a collection element by element, anything else through its
    /// invariant text, so the result is a string or a string array whatever the value's type.
    /// </summary>
    public static object Redact(Redactor redactor, object value)
    {
        if (value is IEnumerable items and not string)
        {
            List<string> redacted = [];

            foreach (var item in items)
            {
                redacted.Add(item is null ? "(null)" : redactor.Redact(item));
            }

            return redacted.ToArray();
        }

        return redactor.Redact(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }
}
