// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Coordination;

/// <summary>
/// Resolves the effective cadence of the dead-owner reconcile backstop from an
/// <see cref="IDeadOwnerReclaimer.ReconcileInterval"/> and the coordination options. The recovery bridge applies it
/// to every registered reclaimer; a feature that runs its own sweep on the same cadence calls it to get the value the
/// bridge uses.
/// </summary>
[PublicAPI]
public static class DeadOwnerReconcileInterval
{
    /// <summary>Returns the effective reconcile interval.</summary>
    /// <param name="configured">The feature's configured interval, or <see langword="null"/> for the derived default.</param>
    /// <param name="coordination">
    /// The coordination options, or <see langword="null"/> when the host runs no coordination provider. Pass
    /// <see langword="null"/> rather than default options in that case: the options system resolves defaults even
    /// without a provider, and clamping against a window no store enforces would shorten a configured interval for
    /// nothing.
    /// </param>
    /// <returns>
    /// <paramref name="configured"/> when it is at most half of <see cref="CoordinationOptions.DeadRetentionWindow"/>
    /// (or <paramref name="coordination"/> is <see langword="null"/>), half that window when it is longer, and half
    /// the window of default <see cref="CoordinationOptions"/> when <paramref name="configured"/> is
    /// <see langword="null"/> and <paramref name="coordination"/> is <see langword="null"/> too.
    /// </returns>
    public static TimeSpan Resolve(TimeSpan? configured, CoordinationOptions? coordination)
    {
        return Resolve(configured, coordination, NullLogger.Instance);
    }

    /// <summary>Returns the effective reconcile interval, logging a warning when a configured value is clamped.</summary>
    internal static TimeSpan Resolve(TimeSpan? configured, CoordinationOptions? coordination, ILogger logger)
    {
        // Relational membership providers prune a dead identity DeadRetentionWindow after DeadThreshold, so Dead is
        // observable for only that window. The reconcile is documented as the authoritative backstop when a NodeLeft
        // event is missed — with a reconcile interval wider than half the visibility window, a dead owner has roughly
        // coin-flip odds of ever being observed Dead. Clamp rather than validate: the interval lives in the feature's
        // options and the window in the coordination options, and neither owns the other.
        var retentionWindow = (coordination ?? new CoordinationOptions()).DeadRetentionWindow;
        var ceiling = TimeSpan.FromTicks(retentionWindow.Ticks / 2);

        if (configured is not { } explicitInterval)
        {
            return ceiling;
        }

        if (coordination is null || ceiling <= TimeSpan.Zero || explicitInterval <= ceiling)
        {
            return explicitInterval;
        }

        logger.DeadNodeReconcileIntervalClamped(explicitInterval, ceiling, retentionWindow);
        return ceiling;
    }
}
