// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>
/// Builder used by <c>AddHeadlessSms</c> to select exactly one SMS provider.
/// </summary>
/// <remarks>
/// Provider packages contribute <c>Use{Provider}</c> extension members on this type (for example
/// <c>UseTwilio</c> from <c>Headless.Sms.Twilio</c> or <c>UseDev</c> from <c>Headless.Sms.Dev</c>). The SMS
/// feature has no shared cross-provider options, so the builder's only job is to collect the chosen provider
/// for the one-provider gate.
/// </remarks>
[PublicAPI]
public sealed class HeadlessSmsSetupBuilder
{
    internal HeadlessSmsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<ISmsProviderOptionsExtension> Extensions { get; } = new List<ISmsProviderOptionsExtension>();

    /// <summary>Registers a provider extension. Called by provider <c>Use{Provider}</c> members.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(ISmsProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
