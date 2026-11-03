// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// Maps a tenant to the edition it subscribes to, so <see cref="EditionFeatureValueProvider"/> can resolve edition
/// values where no principal carries the edition claim: a job or a message consumer that restored the tenant, or a
/// host administrator reading or writing another tenant's features.
/// </summary>
/// <remarks>
/// <para>
/// Optional. Register one implementation as a singleton (<c>services.AddSingleton&lt;ITenantEditionResolver, …&gt;()</c>).
/// The edition claim on the current principal stays the fast path whenever the principal belongs to the ambient
/// tenant; the resolver is asked only when it does not, or when there is no principal.
/// </para>
/// <para>
/// The framework calls it on every such feature read, so an implementation that reads a store should cache its
/// answer.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITenantEditionResolver
{
    /// <summary>Returns the edition <paramref name="tenantId"/> subscribes to.</summary>
    /// <param name="tenantId">The tenant identifier, as <c>ICurrentTenant.Id</c> holds it.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The edition identifier, or <see langword="null"/> when the tenant has no edition.</returns>
    ValueTask<string?> GetEditionIdAsync(string tenantId, CancellationToken cancellationToken = default);
}
