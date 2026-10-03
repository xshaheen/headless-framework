// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>Configures the startup check that benchmarks the selected algorithm.</summary>
/// <remarks>
/// The check hashes once to warm up, then three more times, and compares the median against
/// [<see cref="MinimumDuration" />, <see cref="MaximumDuration" />]. Too fast means test-grade parameters reached a
/// real environment; too slow means every sign-in will stall.
/// </remarks>
[PublicAPI]
public sealed class SecretHasherCostCheckOptions
{
    /// <summary>Gets or sets what happens when the measured cost is out of range. Defaults to <see cref="SecretHasherCostCheckMode.Warn" />.</summary>
    /// <remarks>
    /// The default is the same in every environment. Test-grade parameters reaching production are exactly what this
    /// check exists to catch, so switching it off in production would silence its most valuable signal, and a warning
    /// runs after startup, so it cannot delay or block a rollout.
    /// </remarks>
    public SecretHasherCostCheckMode Mode { get; set; } = SecretHasherCostCheckMode.Warn;

    /// <summary>Gets or sets the shortest acceptable hash duration. Defaults to 5 milliseconds.</summary>
    public TimeSpan MinimumDuration { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>Gets or sets the longest acceptable hash duration. Defaults to 1 second.</summary>
    public TimeSpan MaximumDuration { get; set; } = TimeSpan.FromSeconds(1);
}
