// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.OpenTelemetry;

/// <summary>
/// Adds custom tags to a messaging <see cref="Activity"/> span.
/// Implementations are called for every span type after the built-in framework tags are set.
/// An enricher that throws is isolated: a warning is logged and the next enricher runs normally.
/// </summary>
[PublicAPI]
public interface IActivityTagEnricher
{
    /// <summary>
    /// Called when a messaging span is starting. Add tags to <paramref name="activity"/> as needed.
    /// </summary>
    /// <param name="activity">The span being started. Never <see langword="null"/>.</param>
    /// <param name="context">Contextual data for the current event. Passed by reference; do not store.</param>
    void Enrich(Activity activity, in MessagingEnrichmentContext context);
}
