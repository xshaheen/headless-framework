// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Diagnostics;
using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.MultiTenancy;

/// <summary>
/// Attaches the tenant id to logs, traces, and metrics as configured by <see cref="TenantTelemetryOptions"/>.
/// </summary>
/// <remarks>
/// The tenancy entry points call <see cref="Enrich"/> right after they set the ambient tenant. It is deliberately
/// not part of <see cref="ICurrentTenant.Change"/>: that swap runs in tight loops (per-tenant migrations, fan-out)
/// where a logging scope per call would be pure overhead.
/// </remarks>
[PublicAPI]
public static class TenantTelemetry
{
    /// <summary>
    /// Tags <see cref="Activity.Current"/> with the tenant id and opens a logging scope carrying it, as enabled by
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="logger">
    /// Any logger from the host's logger factory; scopes are shared by every logger the factory creates.
    /// <see langword="null"/> skips the logging scope.
    /// </param>
    /// <param name="options">The telemetry options.</param>
    /// <param name="tenantId">The tenant id that was just made ambient.</param>
    /// <returns>
    /// The logging scope to dispose when the tenant's work ends, or <see langword="null"/> when no scope was opened.
    /// The span tag is not removed on dispose: the span is exported after the entry point returns and must keep it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="tenantId"/> is <see langword="null"/>.</exception>
    public static IDisposable? Enrich(ILogger? logger, TenantTelemetryOptions options, string tenantId)
    {
        TagActivity(Activity.Current, options, tenantId);

        return BeginLogScope(logger, options, tenantId);
    }

    /// <summary>Tags <paramref name="activity"/> with the tenant id when trace enrichment is enabled.</summary>
    /// <param name="activity">The span to tag; <see langword="null"/> or an unrecorded span is ignored.</param>
    /// <param name="options">The telemetry options.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="tenantId"/> is <see langword="null"/>.</exception>
    public static void TagActivity(Activity? activity, TenantTelemetryOptions options, string tenantId)
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(tenantId);

        if (options.EnrichTraces && activity is { IsAllDataRequested: true })
        {
            activity.SetTag(options.AttributeName, tenantId);
        }
    }

    /// <summary>Opens a logging scope carrying the tenant id when log enrichment is enabled.</summary>
    /// <param name="logger">Any logger from the host's logger factory; <see langword="null"/> opens nothing.</param>
    /// <param name="options">The telemetry options.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <returns>The scope to dispose, or <see langword="null"/> when none was opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="tenantId"/> is <see langword="null"/>.</exception>
    public static IDisposable? BeginLogScope(ILogger? logger, TenantTelemetryOptions options, string tenantId)
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(tenantId);

        return options.EnrichLogs && logger is not null
            ? logger.BeginScope(new TenantLogScope(options.LogScopePropertyName, tenantId))
            : null;
    }

    /// <summary>
    /// Gets the metric tag for the ambient tenant, for application meters:
    /// <c>if (TenantTelemetry.TryGetMetricTag(currentTenant, options, out var tag)) { counter.Add(1, tag); }</c>.
    /// </summary>
    /// <param name="currentTenant">The ambient tenant accessor.</param>
    /// <param name="options">The telemetry options.</param>
    /// <param name="tag">The <see cref="TenantTelemetryOptions.AttributeName"/> / tenant id pair.</param>
    /// <returns>
    /// <see langword="true"/> when metric enrichment is enabled and a tenant is ambient; otherwise
    /// <see langword="false"/>, and the measurement should be recorded without a tenant tag.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="currentTenant"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static bool TryGetMetricTag(
        ICurrentTenant currentTenant,
        TenantTelemetryOptions options,
        out KeyValuePair<string, object?> tag
    )
    {
        Argument.IsNotNull(currentTenant);
        Argument.IsNotNull(options);

        if (options.EnrichMetrics && currentTenant.Id is { } tenantId)
        {
            tag = new(options.AttributeName, tenantId);
            return true;
        }

        tag = default;
        return false;
    }

    /// <summary>
    /// A one-property scope state. Logging providers read scopes as key/value lists (OpenTelemetry turns each pair
    /// into a log record attribute), and a dedicated type keeps the scope to a single allocation.
    /// </summary>
    private sealed class TenantLogScope(string key, string tenantId) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 1;

        public KeyValuePair<string, object?> this[int index] =>
            index == 0 ? new(key, tenantId) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return $"{key}:{tenantId}";
        }
    }
}
