// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DataProtection;

/// <summary>
/// Options for <c>ValidateKeyRing</c>, the opt-in warm-up that reads the data-protection key ring after the host
/// starts so a missing container or key surfaces at deploy time instead of at the first protected operation.
/// </summary>
[PublicAPI]
public sealed class DataProtectionKeyRingValidationOptions
{
    /// <summary>
    /// How a validation failure affects the host. Defaults to <see cref="KeyRingCheck.GateReadiness"/>, which
    /// keeps the host unready and retries until the key store works; <see cref="KeyRingCheck.LogOnly"/> runs
    /// once, logs a failure at <c>Critical</c> level, and never gates readiness.
    /// </summary>
    public KeyRingCheck Check { get; set; } = KeyRingCheck.GateReadiness;
}
