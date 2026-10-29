// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Search;

/// <summary>
/// Configures search during <c>AddHeadlessSearch(setup =&gt; …)</c>: exactly one provider, chosen through a
/// <c>Use…</c> call such as <c>UsePostgreSql</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessSearchSetupBuilder
{
    internal HeadlessSearchSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<ISearchProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>
    /// Registers a provider extension whose services are added when setup completes. Called by provider packages'
    /// <c>Use…</c> members rather than by application code.
    /// </summary>
    /// <param name="extension">The provider extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(ISearchProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
