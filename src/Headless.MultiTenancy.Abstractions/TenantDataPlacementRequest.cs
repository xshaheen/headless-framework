// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// What <see cref="ITenantDataPlacementResolver"/> is asked to place: one tenant's data for one routed data store.
/// </summary>
/// <remarks>
/// The data store lets one tenant's contexts live in different places (orders in one database, billing in another).
/// A routed context belongs to <see cref="DefaultDataStore"/> unless its routing options name another, so a
/// resolver that places every context of a tenant alike can ignore <see cref="DataStore"/>.
/// </remarks>
[PublicAPI]
public sealed record TenantDataPlacementRequest
{
    /// <summary>The data store a routed context belongs to when its routing options name none.</summary>
    public const string DefaultDataStore = "default";

    /// <summary>Initializes a request for <paramref name="tenantId"/>'s placement in <paramref name="dataStore"/>.</summary>
    /// <param name="tenantId">The canonical tenant id.</param>
    /// <param name="dataStore">The routed data store, or <see langword="null"/> for <see cref="DefaultDataStore"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is empty or white space, or <paramref name="dataStore"/> is empty or white space.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is <see langword="null"/>.</exception>
    public TenantDataPlacementRequest(string tenantId, string? dataStore = null)
    {
        TenantId = Argument.IsNotNullOrWhiteSpace(tenantId);
        DataStore = dataStore is null ? DefaultDataStore : Argument.IsNotNullOrWhiteSpace(dataStore);
    }

    /// <summary>The canonical tenant id. See <see cref="TenantInfo.Id"/>.</summary>
    public string TenantId { get; }

    /// <summary>The routed data store the placement is for; <see cref="DefaultDataStore"/> unless a context names another.</summary>
    public string DataStore { get; }
}
