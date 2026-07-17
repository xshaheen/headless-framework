// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Adds custom tags to a messaging <see cref="Activity"/> span.
/// Implementations are called for every span type after the built-in framework tags are set.
/// An enricher that throws is isolated: a warning is logged and the next enricher runs normally.
/// </summary>
[PublicAPI]
public interface IActivityTagEnricher
{
    /// <summary>
    /// Called synchronously while a messaging span is starting. Add tags to <paramref name="activity"/> as needed.
    /// </summary>
    /// <param name="activity">The span being started. Never <see langword="null"/>.</param>
    /// <param name="context">Contextual data for the current event. Passed by reference; do not store.</param>
    /// <remarks>
    /// <para>
    /// Enrichers run <strong>synchronously</strong> at span start, so every tag they add is guaranteed to be
    /// attached before the span can end. Implementations must not perform blocking I/O; pre-resolve any data
    /// (for example ambient context or a cache lookup) and finish synchronously.
    /// </para>
    /// <para>
    /// Exceptions are caught and logged (when a logger is wired) without breaking the messaging operation; the
    /// next enricher still runs.
    /// </para>
    /// <para>
    /// The <c>context.Headers</c> dictionary is the raw wire-header dictionary of the underlying
    /// message. It may contain authentication tokens, OpenTelemetry propagator state, internal
    /// IDs, or other personally identifiable information (PII). <strong>Do not serialize
    /// <c>Headers</c> contents wholesale onto activity tags.</strong> Read only the specific
    /// keys your enricher requires.
    /// </para>
    /// <para>
    /// Enrichers must not write tags in the following reserved namespaces; the framework or the
    /// OpenTelemetry SDK will silently overwrite them and produce inconsistent traces:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>messaging.*</c> — OpenTelemetry messaging semantic conventions.</description></item>
    /// <item><description><c>server.*</c> — broker / destination addressing.</description></item>
    /// <item><description>
    /// <c>headless.messaging.*</c> — framework-emitted ground truth (currently
    /// <c>tenant_id</c> and <c>retry_count</c>).
    /// </description></item>
    /// <item><description><c>exception.*</c> — OpenTelemetry exception convention emitted at activity stop.</description></item>
    /// </list>
    /// <para>
    /// Use an application- or product-scoped namespace (for example <c>app.tenant.region</c> or
    /// <c>product.feature.flag</c>) for custom enricher tags.
    /// </para>
    /// </remarks>
    void Enrich(Activity activity, in MessagingEnrichmentContext context);
}
