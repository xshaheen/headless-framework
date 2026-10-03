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
