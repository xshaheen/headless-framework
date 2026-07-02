// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Sms;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering Headless SMS senders.
/// </summary>
[PublicAPI]
public static class SetupSmsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless SMS senders from a single setup builder. Provider packages contribute the
        /// default (unkeyed) sender through <c>Use*</c> extensions on <see cref="HeadlessSmsSetupBuilder"/>
        /// (for example <c>UseTwilio</c>, <c>UseAwsSns</c>, <c>UseCequens</c>, <c>UseDevelopment</c>, <c>UseNoop</c>)
        /// and named senders through <c>setup.AddNamed(name, i =&gt; i.Use*(…))</c>. A default sender is
        /// optional (at most one); named senders are optional and unbounded. Contributions are queued and not
        /// run until the setup gates pass, so a setup that fails a gate leaves the service collection unchanged
        /// (provider <c>Use*</c> members also validate their inputs synchronously before queuing).
        /// </summary>
        /// <param name="configure">Delegate that selects the default sender and any named senders.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the delegate registers more than one default provider, configures a named instance with
        /// zero or multiple providers, reuses a name, or when <c>AddHeadlessSms</c> has already been called on
        /// the same <see cref="IServiceCollection"/>.
        /// </exception>
        public IServiceCollection AddHeadlessSms(Action<HeadlessSmsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSmsSetupBuilder(services);
            configure(setup);

            return _AddSmsProviderCore(services, setup);
        }
    }

    private static IServiceCollection _AddSmsProviderCore(IServiceCollection services, HeadlessSmsSetupBuilder setup)
    {
        if (setup.DefaultExtensions.Count > 1)
        {
            throw new InvalidOperationException(
                "Headless.Sms allows at most one default SMS provider. Multiple default providers were "
                    + "configured — register the additional senders as named instances with `AddNamed`."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(SmsProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessSms was already called on this service collection. Configure all SMS senders "
                    + "(default and named) in a single AddHeadlessSms call."
            );
        }

        services.AddSingleton(new SmsProviderRegistration());

        var registeredNames = setup.InstanceNames.ToFrozenSet(StringComparer.Ordinal);
        services.TryAddSingleton<ISmsSenderProvider>(provider => new KeyedServiceSmsSenderProvider(
            provider,
            registeredNames
        ));

        // Default first, then each named instance — nothing touched `services` until the gates above passed.
        foreach (var action in setup.DefaultExtensions)
        {
            action(services);
        }

        foreach (var (_, action) in setup.NamedExtensions)
        {
            action(services);
        }

        return services;
    }

    private sealed record SmsProviderRegistration;
}
