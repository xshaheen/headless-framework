// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Settings.Definitions;
using Headless.Settings.Values;

namespace Headless.Settings.ClientVisibility;

/// <summary>
/// Reads the values of every setting whose definition is <see cref="Models.SettingDefinition.IsVisibleToClients"/>,
/// for example to include in the configuration an application returns to its front end.
/// </summary>
[PublicAPI]
public interface IClientVisibleSettingsReader
{
    /// <summary>Resolves the client-visible setting values for the principal and tenant in <paramref name="context"/>.</summary>
    /// <param name="context">The principal and tenant to resolve the values for.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>
    /// The value of each client-visible setting, keyed by setting name. A setting with no value maps to
    /// <see langword="null"/>. An encrypted setting that is visible to clients is returned decrypted, so mark a secret
    /// visible only when the client is meant to read it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyDictionary<string, string?>> GetAsync(
        PrincipalContext context,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Default <see cref="IClientVisibleSettingsReader"/>.</summary>
internal sealed class ClientVisibleSettingsReader(
    ISettingDefinitionManager definitionManager,
    ISettingManager settingManager,
    ICurrentTenant currentTenant,
    // Optional: only Headless.Api.ServiceDefaults registers an accessor. A host without one has no ambient
    // principal for the user value provider to read, so there is nothing to switch.
    ICurrentPrincipalAccessor? principalAccessor = null
) : IClientVisibleSettingsReader
{
    public async Task<IReadOnlyDictionary<string, string?>> GetAsync(
        PrincipalContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);

        // The user and tenant value providers read the ambient identity, so resolve under the context's identity.
        using var principalScope = principalAccessor?.Change(context.Principal);
        using var tenantScope = currentTenant.Change(context.TenantId);

        var definitions = await definitionManager.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var names = definitions
            .Where(definition => definition.IsVisibleToClients)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.Ordinal);

        // The batch read rejects an empty name set.
        if (names.Count == 0)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var values = await settingManager.GetAllAsync(names, cancellationToken).ConfigureAwait(false);

        return values.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
    }
}
