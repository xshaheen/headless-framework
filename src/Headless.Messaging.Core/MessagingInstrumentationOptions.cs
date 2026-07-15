// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Configures messaging span enrichment and the framework's built-in tag enrichers. Exposed on the messaging
/// setup builder (<c>setup.Instrumentation</c>) — the enricher pipeline runs natively inside
/// <c>Headless.Messaging.Core</c>, so registration happens at <c>AddHeadlessMessaging</c> time rather than at
/// OpenTelemetry-registration time.
/// </summary>
[PublicAPI]
public sealed class MessagingInstrumentationOptions
{
    private readonly List<IActivityTagEnricher> _enrichers = [];

    /// <summary>
    /// When <see langword="true"/>, the built-in <c>headless.messaging.tenant_id</c> tag enricher
    /// is not registered, so tenant identifiers are not written to messaging activity spans.
    /// Default: <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// In shared multi-tenant trace backends, the <c>headless.messaging.tenant_id</c> tag becomes
    /// visible across tenants — any operator with access to the trace store sees every tenant's
    /// IDs. Set this to <see langword="true"/> to opt out of tenant-ID tagging, or pair the
    /// framework with a tenant-scoped trace exporter so each tenant only sees its own spans.
    /// </remarks>
    public bool SuppressTenantIdTag { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the built-in <c>headless.messaging.retry_count</c> tag enricher
    /// is not registered, so retry counts are not written to subscriber-invoke activity spans.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool SuppressRetryCountTag { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the built-in intent tag enricher is not registered, so
    /// <c>headless.messaging.intent</c> and <c>messaging.destination.kind</c> are not written
    /// to messaging activity spans. Default: <see langword="false"/>.
    /// </summary>
    public bool SuppressIntentTags { get; set; }

    /// <summary>
    /// Custom enrichers appended after the built-in enrichers. Enrichers are invoked in insertion
    /// order for every span type. The collection is snapshotted at registration time
    /// (<c>AddHeadlessMessaging</c>); changes after registration are ignored.
    /// </summary>
    /// <remarks>
    /// Built-in enrichers run first, in the following order:
    /// <list type="number">
    /// <item><description><c>TenantIdTagEnricher</c> (unless <see cref="SuppressTenantIdTag"/> is <see langword="true"/>).</description></item>
    /// <item><description><c>IntentTagEnricher</c> (unless <see cref="SuppressIntentTags"/> is <see langword="true"/>).</description></item>
    /// <item><description><c>RetryCountTagEnricher</c> (unless <see cref="SuppressRetryCountTag"/> is <see langword="true"/>).</description></item>
    /// </list>
    /// Custom enrichers added via <see cref="AddEnricher"/> are appended after the built-ins, in
    /// the order they were added.
    /// </remarks>
    public IReadOnlyList<IActivityTagEnricher> Enrichers => _enrichers;

    /// <summary>
    /// Appends a custom enricher to be invoked after the built-in enrichers.
    /// </summary>
    /// <param name="enricher">The enricher to add. Must not be <see langword="null"/>.</param>
    /// <returns>The same options instance for chaining.</returns>
    public MessagingInstrumentationOptions AddEnricher(IActivityTagEnricher enricher)
    {
        Argument.IsNotNull(enricher);
        _enrichers.Add(enricher);
        return this;
    }

    /// <summary>
    /// Builds the snapshot of enrichers to register for the current options state. Returns the built-in
    /// enrichers (gated by <see cref="SuppressTenantIdTag"/>, <see cref="SuppressIntentTags"/>, and
    /// <see cref="SuppressRetryCountTag"/>) followed by any custom enrichers added via
    /// <see cref="AddEnricher"/>, in registration order.
    /// </summary>
    /// <remarks>
    /// Use this to assert composition in tests or to introspect the active enricher set in
    /// diagnostic tooling. The result is a fresh snapshot — mutating <see cref="AddEnricher"/>
    /// after calling this does not affect previously returned arrays.
    /// </remarks>
    [Pure]
    public IActivityTagEnricher[] BuildEnrichers()
    {
        var list = new List<IActivityTagEnricher>();

        if (!SuppressTenantIdTag)
        {
            list.Add(new TenantIdTagEnricher());
        }

        if (!SuppressIntentTags)
        {
            list.Add(new IntentTagEnricher());
        }

        if (!SuppressRetryCountTag)
        {
            list.Add(new RetryCountTagEnricher());
        }

        list.AddRange(_enrichers);

        return [.. list];
    }
}
