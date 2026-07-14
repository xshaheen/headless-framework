// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.DataProtection;

/// <summary>
/// Options for <c>ValidateKeyRingAtStartup</c>, the opt-in hosted service that exercises the data-protection key
/// ring at host start so misconfiguration surfaces at deploy time instead of at the first (lazy) key write.
/// </summary>
[PublicAPI]
public sealed class DataProtectionStartupValidationOptions
{
    /// <summary>
    /// How a validation failure is surfaced. Defaults to <see cref="StartupValidationMode.Throw"/>, which fails host
    /// startup; <see cref="StartupValidationMode.LogOnly"/> logs at <c>Critical</c> level and continues.
    /// </summary>
    public StartupValidationMode Mode { get; set; } = StartupValidationMode.Throw;

    /// <summary>
    /// Whether to verify write access with a real write each startup by uploading and deleting a reserved sentinel
    /// blob in the <c>DataProtection</c> container through the same ensure + retry pipeline the key writes use.
    /// Defaults to <see langword="true"/>. This is what catches lost write permission or a deleted container when
    /// the key ring already holds a valid key — a state in which the protect/unprotect round-trip performs no write
    /// — and it is the only write-path guarantee on nodes with <c>AutoGenerateKeys</c> disabled. Only applies when
    /// the configured repository is the blob-storage repository; otherwise the probe is skipped with a debug log.
    /// </summary>
    public bool ProbeWritePath { get; set; } = true;
}
