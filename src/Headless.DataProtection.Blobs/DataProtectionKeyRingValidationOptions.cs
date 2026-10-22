// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DataProtection;

/// <summary>
/// Options for <c>ValidateKeyRing</c>, the opt-in warm-up that exercises the data-protection key ring after the host
/// starts so a misconfiguration surfaces at deploy time instead of at the first (lazy) key write.
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

    /// <summary>
    /// Whether to verify write access with a real write on each validation by uploading and deleting a reserved
    /// sentinel blob in the <c>DataProtection</c> container through the same ensure + retry pipeline the key writes
    /// use. Defaults to <see langword="true"/>. This is what catches lost write permission or a deleted container when
    /// the key ring already holds a valid key — a state in which the protect/unprotect round-trip performs no write
    /// — and it is the only write-path guarantee on nodes with <c>AutoGenerateKeys</c> disabled. Only applies when
    /// the configured repository is the blob-storage repository; otherwise the probe is skipped with a debug log.
    /// </summary>
    public bool ProbeWritePath { get; set; } = true;
}
