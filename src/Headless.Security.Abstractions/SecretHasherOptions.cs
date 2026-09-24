// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>Configures <see cref="ISecretHasher" />.</summary>
[PublicAPI]
public sealed class SecretHasherOptions
{
    /// <summary>
    /// Gets or sets the PHC identifier of the algorithm new hashes use. Defaults to
    /// <see cref="SecretHashAlgorithms.Argon2id" />, which requires the <c>Headless.Security.Argon2</c> package; startup
    /// fails when the configured algorithm has no registered implementation.
    /// </summary>
    /// <remarks>
    /// Changing this value never invalidates stored hashes. <see cref="ISecretHasher.Verify" /> accepts every
    /// registered algorithm and returns a rehash under the configured one.
    /// </remarks>
    public string Algorithm { get; set; } = SecretHashAlgorithms.Argon2id;

    /// <summary>
    /// Gets or sets the maximum secret length in UTF-16 code units. Longer secrets are rejected before hashing, which
    /// keeps a caller from spending unbounded work on attacker-sized input. Defaults to 1024.
    /// </summary>
    public int MaxSecretLength { get; set; } = 1024;

    /// <summary>Gets or sets the Argon2id cost parameters. Defaults to the OWASP baseline (19 MiB, 2 iterations).</summary>
    public Argon2idHashParameters Argon2id { get; set; } = new();

    /// <summary>Gets or sets the PBKDF2-SHA256 cost parameters. Defaults to 600,000 iterations (OWASP).</summary>
    public Pbkdf2HashParameters Pbkdf2Sha256 { get; set; } = new();

    /// <summary>Gets or sets the startup check that measures the configured algorithm's hash duration.</summary>
    public SecretHasherCostCheckOptions CostCheck { get; set; } = new();
}

/// <summary>Argon2id cost parameters.</summary>
/// <remarks>
/// Parallelism is fixed at 1 and the salt at 16 bytes: the libsodium implementation derives only that shape, and the
/// OWASP baseline already uses a single lane.
/// </remarks>
[PublicAPI]
public sealed class Argon2idHashParameters
{
    /// <summary>
    /// Gets or sets the memory size in KiB. Must be between <see cref="SecretHashLimits.MinArgon2idMemorySize" /> and
    /// <see cref="SecretHashLimits.MaxArgon2idMemorySize" />. Defaults to 19,456 (19 MiB).
    /// </summary>
    public int MemorySize { get; set; } = 19_456;

    /// <summary>
    /// Gets or sets the number of passes over memory. Must be between
    /// <see cref="SecretHashLimits.MinArgon2idIterations" /> and <see cref="SecretHashLimits.MaxArgon2idIterations" />.
    /// Defaults to 2.
    /// </summary>
    public int Iterations { get; set; } = 2;

    /// <summary>Gets or sets the hash length in bytes. Must be between 16 and 64. Defaults to 32.</summary>
    public int HashSize { get; set; } = 32;
}

/// <summary>PBKDF2-SHA256 cost parameters.</summary>
[PublicAPI]
public sealed class Pbkdf2HashParameters
{
    /// <summary>
    /// Gets or sets the iteration count. Must be between <see cref="SecretHashLimits.MinPbkdf2Iterations" /> and
    /// <see cref="SecretHashLimits.MaxPbkdf2Iterations" />. Defaults to 600,000.
    /// </summary>
    public int Iterations { get; set; } = 600_000;

    /// <summary>Gets or sets the salt length in bytes. Must be between 16 and 64. Defaults to 16.</summary>
    public int SaltSize { get; set; } = 16;

    /// <summary>Gets or sets the hash length in bytes. Must be between 16 and 64. Defaults to 32.</summary>
    public int HashSize { get; set; } = 32;
}

/// <summary>Configures the startup check that benchmarks the configured algorithm.</summary>
/// <remarks>
/// The check hashes once to warm up, then three more times, and compares the median against
/// [<see cref="MinimumDuration" />, <see cref="MaximumDuration" />]. Too fast means test-grade parameters reached a
/// real environment; too slow means every sign-in will stall.
/// </remarks>
[PublicAPI]
public sealed class SecretHasherCostCheckOptions
{
    /// <summary>Gets or sets what happens when the measured cost is out of range. Defaults to <see cref="SecretHasherCostCheckMode.Warn" />.</summary>
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
