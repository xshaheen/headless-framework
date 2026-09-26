// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;

namespace Headless.Testing.Helpers;

/// <summary>
/// Two tenants, A and B, over one <see cref="ICurrentTenant"/>, with disposable scopes that make either tenant
/// (or the host) current. Isolation tests seed data as <see cref="TenantA"/> and probe it as <see cref="TenantB"/>.
/// </summary>
/// <remarks>
/// The world only changes the ambient tenant id; it writes nothing to a tenant catalog. EF Core's tenant query
/// filter and write guard key on that id alone. An application that also resolves tenants over HTTP by identifier
/// seeds the same ids into its catalog store, for example through <c>UseInMemory(...)</c> options.
/// </remarks>
[PublicAPI]
public sealed class TenantWorld
{
    /// <summary>The tenant id <see cref="Create"/> uses for tenant A when none is given.</summary>
    public const string DefaultTenantA = "tenant-a";

    /// <summary>The tenant id <see cref="Create"/> uses for tenant B when none is given.</summary>
    public const string DefaultTenantB = "tenant-b";

    /// <summary>Creates a world over the <paramref name="currentTenant"/> the code under test reads.</summary>
    /// <param name="currentTenant">
    /// The tenant context the code under test reads: a <see cref="TestCurrentTenant"/>, or the application's own
    /// ambient <see cref="ICurrentTenant"/>.
    /// </param>
    /// <param name="tenantA">The tenant that owns the data under test.</param>
    /// <param name="tenantB">The tenant that must not reach tenant A's data. Must differ from <paramref name="tenantA"/>.</param>
    /// <exception cref="ArgumentException">Either id is blank, or both ids are equal.</exception>
    public TenantWorld(ICurrentTenant currentTenant, string tenantA = DefaultTenantA, string tenantB = DefaultTenantB)
    {
        CurrentTenant = Argument.IsNotNull(currentTenant);
        TenantA = Argument.IsNotNullOrWhiteSpace(tenantA);
        TenantB = Argument.IsNotNullOrWhiteSpace(tenantB);
        // Equal ids would turn every cross-tenant probe into a same-tenant read that passes for the wrong reason.
        Argument.IsNotEqualTo(tenantB, tenantA, StringComparer.Ordinal, "Tenant B must differ from tenant A.");
    }

    /// <summary>The tenant context every scope changes.</summary>
    public ICurrentTenant CurrentTenant { get; }

    /// <summary>The tenant that owns the data under test.</summary>
    public string TenantA { get; }

    /// <summary>The tenant that must not reach tenant A's data.</summary>
    public string TenantB { get; }

    /// <summary>Creates a world over a fresh <see cref="TestCurrentTenant"/> with no tenant current.</summary>
    /// <param name="tenantA">The tenant that owns the data under test.</param>
    /// <param name="tenantB">The tenant that must not reach tenant A's data.</param>
    /// <returns>The world. Register <see cref="CurrentTenant"/> as the <see cref="ICurrentTenant"/> the code under test reads.</returns>
    public static TenantWorld Create(string tenantA = DefaultTenantA, string tenantB = DefaultTenantB)
    {
        return new TenantWorld(new TestCurrentTenant(), tenantA, tenantB);
    }

    /// <summary>Makes tenant A current until the returned scope is disposed.</summary>
    /// <returns>A scope that restores the previous tenant when disposed.</returns>
    [MustDisposeResource]
    public IDisposable AsTenantA() => CurrentTenant.Change(TenantA);

    /// <summary>Makes tenant B current until the returned scope is disposed.</summary>
    /// <returns>A scope that restores the previous tenant when disposed.</returns>
    [MustDisposeResource]
    public IDisposable AsTenantB() => CurrentTenant.Change(TenantB);

    /// <summary>Makes <paramref name="tenantId"/> current until the returned scope is disposed.</summary>
    /// <param name="tenantId">Any tenant id, including one outside this world.</param>
    /// <returns>A scope that restores the previous tenant when disposed.</returns>
    [MustDisposeResource]
    public IDisposable AsTenant(string tenantId) => CurrentTenant.Change(Argument.IsNotNullOrWhiteSpace(tenantId));

    /// <summary>Clears the current tenant (host context) until the returned scope is disposed.</summary>
    /// <returns>A scope that restores the previous tenant when disposed.</returns>
    [MustDisposeResource]
    public IDisposable AsHost() => CurrentTenant.Change(id: null);

    /// <summary>
    /// Creates an authenticated principal whose <see cref="UserClaimTypes.TenantId"/> claim names
    /// <paramref name="tenantId"/>, for test authentication handlers that sign a client in as one tenant's user.
    /// </summary>
    /// <param name="tenantId">The tenant the principal belongs to.</param>
    /// <param name="userId">The user id claim. Defaults to a user unique to <paramref name="tenantId"/>.</param>
    /// <param name="authenticationType">The identity's authentication type; any non-empty value marks it authenticated.</param>
    /// <returns>The principal.</returns>
    public static ClaimsPrincipal CreatePrincipal(
        string tenantId,
        string? userId = null,
        string authenticationType = "Test"
    )
    {
        Argument.IsNotNullOrWhiteSpace(tenantId);
        Argument.IsNotNullOrWhiteSpace(authenticationType);

        var identity = new ClaimsIdentity(
            [
                new Claim(UserClaimTypes.TenantId, tenantId),
                new Claim(UserClaimTypes.UserId, userId ?? $"{tenantId}-user"),
            ],
            authenticationType
        );

        return new ClaimsPrincipal(identity);
    }
}
