// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
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
}
