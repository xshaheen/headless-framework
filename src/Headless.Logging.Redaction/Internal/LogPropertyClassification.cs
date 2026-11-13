// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Logging.Internal;

/// <summary>A structured log property, by name, that is redacted as if its value carried a classification.</summary>
internal sealed record LogPropertyClassification(string Name, DataClassification Classification)
{
    /// <summary>
    /// The redactor for each classified property name, matched ignoring case the way logging matches names. A name
    /// registered with several classifications is redacted as one combined set, as a member with several attributes is.
    /// </summary>
    public static FrozenDictionary<string, Redactor> Resolve(
        IServiceProvider provider,
        IRedactorProvider? redactorProvider
    )
    {
        if (redactorProvider is null)
        {
            return FrozenDictionary<string, Redactor>.Empty;
        }

        return provider
            .GetServices<LogPropertyClassification>()
            .GroupBy(static c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(
                static group => group.Key,
                group =>
                    redactorProvider.GetRedactor(
                        new DataClassificationSet([.. group.Select(static c => c.Classification).Distinct()])
                    ),
                StringComparer.OrdinalIgnoreCase
            );
    }
}
