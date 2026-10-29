// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

/// <summary>
/// Resolves the cadence of the dead-owner reconcile backstop for a feature that runs a
/// <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>, so every feature derives it from the coordination
/// options the same way.
/// </summary>
internal static class DeadOwnerReconcileInterval
{
    /// <summary>Returns the effective reconcile interval, logging a warning when a configured value is clamped.</summary>
    /// <param name="configured">The feature's configured interval, or <see langword="null"/> for the derived default.</param>
    /// <param name="coordination">The coordination options, or <see langword="null"/> when coordination is not registered.</param>
    /// <param name="logger">The feature's logger, so the warning carries the feature's category.</param>
    /// <param name="membership">
    /// The host's registered membership. <see cref="NullNodeMembership"/> means no coordination provider, so the
    /// coordination options are ignored even though the options system still resolves their defaults.
    /// </param>
    internal static TimeSpan Resolve(
        TimeSpan? configured,
        CoordinationOptions? coordination,
        ILogger logger,
        INodeMembership? membership = null
    )
    {
        if (membership is NullNodeMembership)
        {
            coordination = null;
        }

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
