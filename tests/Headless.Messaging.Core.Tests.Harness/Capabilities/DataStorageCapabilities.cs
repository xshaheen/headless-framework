// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Capabilities;

/// <summary>Flags indicating data storage-specific capabilities for conditional test execution.</summary>
[PublicAPI]
public sealed class DataStorageCapabilities
{
    public static DataStorageCapabilities Default { get; } = new();

    /// <summary>Whether the storage supports distributed locking.</summary>
    public bool SupportsLocking { get; init; } = true;

    /// <summary>Whether the storage supports message expiration/TTL.</summary>
    public bool SupportsExpiration { get; init; } = true;

    /// <summary>Whether the storage supports concurrent operations safely.</summary>
    public bool SupportsConcurrentOperations { get; init; } = true;

    /// <summary>Whether the storage supports delayed message scheduling.</summary>
    public bool SupportsDelayedScheduling { get; init; } = true;

    /// <summary>Whether the storage supports monitoring API.</summary>
    public bool SupportsMonitoringApi { get; init; } = true;
}
