// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Settings.Definitions;
using Headless.Settings.Values;

namespace Headless.Settings.ClientConfig;

/// <summary>
/// Builds the settings section of an application's client config: the values of every setting whose definition
/// is <see cref="Models.SettingDefinition.IsVisibleToClients"/>.
/// </summary>
[PublicAPI]
public interface IClientSettingsConfigBuilder
{
    /// <summary>Resolves the client-visible setting values for the principal and tenant in <paramref name="context"/>.</summary>
    /// <param name="context">The principal and tenant to resolve the values for.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The client-visible setting values, keyed by setting name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    Task<ClientSettingsConfig> BuildAsync(ClientConfigContext context, CancellationToken cancellationToken = default);
}

/// <summary>The settings section of a client config.</summary>
/// <param name="Values">
/// The value of each client-visible setting, keyed by setting name. A setting with no value maps to
/// <see langword="null"/>. An encrypted setting that is visible to clients is returned decrypted, so mark a secret
/// visible only when the client is meant to read it.
/// </param>
[PublicAPI]
public sealed record ClientSettingsConfig(IReadOnlyDictionary<string, string?> Values);

/// <summary>Default <see cref="IClientSettingsConfigBuilder"/>.</summary>
internal sealed class ClientSettingsConfigBuilder(
    ISettingDefinitionManager definitionManager,
    ISettingManager settingManager,
    ICurrentPrincipalAccessor principalAccessor,
    ICurrentTenant currentTenant
) : IClientSettingsConfigBuilder
{
    public async Task<ClientSettingsConfig> BuildAsync(
        ClientConfigContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);

        // The user and tenant value providers read the ambient identity, so resolve under the context's identity.
        using var principalScope = principalAccessor.Change(context.Principal);
        using var tenantScope = currentTenant.Change(context.TenantId);

        var definitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var names = definitions
            .Where(definition => definition.IsVisibleToClients)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);

        // The batch read rejects an empty name set.
        if (names.Count == 0)
        {
            return new ClientSettingsConfig(new Dictionary<string, string?>(StringComparer.Ordinal));
        }

        var values = await settingManager.GetAllAsync(names, cancellationToken).ConfigureAwait(false);

        return new ClientSettingsConfig(
            values.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal)
        );
    }
}
