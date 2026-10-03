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
