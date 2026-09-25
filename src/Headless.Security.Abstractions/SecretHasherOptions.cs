// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>Configures the settings of <see cref="ISecretHasher" /> that hold for every algorithm.</summary>
/// <remarks>
/// Which algorithm writes new hashes, and its cost parameters, are chosen by the <c>Use*</c> call on the secret-hasher
/// setup builder; each algorithm owns its own options type, so adding an algorithm never changes this class.
/// </remarks>
[PublicAPI]
public sealed class SecretHasherOptions
{
    /// <summary>
    /// Gets or sets the maximum secret length in UTF-16 code units. Longer secrets are rejected before hashing, which
    /// keeps a caller from spending unbounded work on attacker-sized input. Defaults to 1024.
    /// </summary>
    public int MaxSecretLength { get; set; } = 1024;

    /// <summary>Gets or sets the startup check that measures the selected algorithm's hash duration.</summary>
    public SecretHasherCostCheckOptions CostCheck { get; set; } = new();
}

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
    /// cannot block a rollout.
    /// </remarks>
    public SecretHasherCostCheckMode Mode { get; set; } = SecretHasherCostCheckMode.Warn;

    /// <summary>Gets or sets the shortest acceptable hash duration. Defaults to 5 milliseconds.</summary>
    public TimeSpan MinimumDuration { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>Gets or sets the longest acceptable hash duration. Defaults to 1 second.</summary>
    public TimeSpan MaximumDuration { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>What the startup cost check does with an out-of-range measurement.</summary>
[PublicAPI]
public enum SecretHasherCostCheckMode
{
    /// <summary>Skip the benchmark entirely.</summary>
    Off = 0,

    /// <summary>Log a warning and continue starting.</summary>
    Warn = 1,

    /// <summary>Fail startup.</summary>
    Strict = 2,
}
