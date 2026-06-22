// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>Root registration entry for the SMS feature.</summary>
[PublicAPI]
public static class SetupSmsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the SMS feature, selecting exactly one provider via the supplied builder callback
        /// (for example <c>setup =&gt; setup.UseTwilio(...)</c> or <c>setup =&gt; setup.UseDev(path)</c>).
        /// </summary>
        /// <param name="configure">Callback that selects the provider on the builder.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Zero or more than one provider was configured, or <c>AddHeadlessSms</c> was already called on this
        /// service collection.
        /// </exception>
        public IServiceCollection AddHeadlessSms(Action<HeadlessSmsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSmsSetupBuilder(services);
            configure(setup);

            return _AddSmsCore(services, setup);
        }
    }

    private static IServiceCollection _AddSmsCore(IServiceCollection services, HeadlessSmsSetupBuilder setup)
    {
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.Extensions.Count == 0
                    ? "Headless.Sms requires exactly one provider. Call one of the `Use{Provider}` builder methods."
                    : "Headless.Sms requires exactly one provider. Multiple providers were configured."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(SmsProviderRegistration)))
        {
            throw new InvalidOperationException(
                "Headless.Sms requires exactly one provider. Multiple providers were configured."
            );
        }

        var extension = setup.Extensions.Single();
        var extensionTypeName = extension.GetType().FullName ?? "unknown";
        services.AddSingleton(new SmsProviderRegistration(extensionTypeName));

        extension.AddServices(services);

        return services;
    }

    private sealed record SmsProviderRegistration(string Provider);
}
