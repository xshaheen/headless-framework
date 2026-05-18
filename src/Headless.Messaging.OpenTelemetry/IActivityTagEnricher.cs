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
    /// <param name="cancellationToken">Cancels any async work the enricher chooses to perform.</param>
    /// <remarks>
    /// <para>
    /// The pipeline awaits enrichers that complete synchronously. Tags added during the
    /// synchronous portion of the returned <see cref="ValueTask"/> are attached to the activity.
    /// </para>
    /// <para>
    /// <strong>Important:</strong> if the returned <see cref="ValueTask"/> does not complete
    /// synchronously, the pipeline does <strong>not</strong> await it — the enricher becomes
    /// fire-and-forget from that point. Any tags attached after the activity stops are dropped
    /// by the OpenTelemetry SDK. Implementations that need async I/O should pre-resolve their
    /// data and finish synchronously (e.g., cache hits, ambient context).
    /// </para>
    /// <para>
    /// Exceptions from synchronous completion are caught and logged (when a logger is wired)
    /// without breaking the messaging operation. Exceptions from the async tail are caught and
    /// logged the same way; the activity may already be stopped by the time the log is emitted.
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
    ValueTask Enrich(
        Activity activity,
        in MessagingEnrichmentContext context,
        CancellationToken cancellationToken = default
    );
}
