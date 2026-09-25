// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Features.Definitions;
using Headless.Features.Values;
using Headless.MultiTenancy;

namespace Headless.Features.ClientConfig;

/// <summary>
/// Builds the features section of an application's client config: the effective values of every feature
/// whose definition is <see cref="Models.FeatureDefinition.IsVisibleToClients"/>.
/// </summary>
[PublicAPI]
public interface IClientFeaturesConfigBuilder
{
    /// <summary>Resolves the client-visible feature values for the principal and tenant in <paramref name="context"/>.</summary>
    /// <param name="context">The principal and tenant to resolve the values for.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The client-visible feature values, keyed by feature name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    Task<ClientFeaturesConfig> BuildAsync(ClientConfigContext context, CancellationToken cancellationToken = default);
}

/// <summary>The features section of a client config.</summary>
/// <param name="Values">
/// The effective value of each client-visible feature, keyed by feature name. A feature with no value maps to
/// <see langword="null"/>.
/// </param>
[PublicAPI]
public sealed record ClientFeaturesConfig(IReadOnlyDictionary<string, string?> Values);

/// <summary>Default <see cref="IClientFeaturesConfigBuilder"/>.</summary>
internal sealed class ClientFeaturesConfigBuilder(
    IFeatureDefinitionManager definitionManager,
    IFeatureManager featureManager,
    ICurrentPrincipalAccessor principalAccessor,
    ICurrentTenant currentTenant
) : IClientFeaturesConfigBuilder
{
    public async Task<ClientFeaturesConfig> BuildAsync(
        ClientConfigContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);

        // The value providers read the ambient tenant and principal, so resolve under the context's identity.
        using var principalScope = principalAccessor.Change(context.Principal);
        using var tenantScope = currentTenant.Change(context.TenantId);

        var definitions = await definitionManager.GetFeaturesAsync(cancellationToken).ConfigureAwait(false);
        var names = definitions
            .Where(definition => definition.IsVisibleToClients)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);

        var values = await featureManager.GetAllAsync(names, cancellationToken).ConfigureAwait(false);

        return new ClientFeaturesConfig(
            values.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal)
        );
    }
}
