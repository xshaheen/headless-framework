// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Emails;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering Headless email senders.
/// </summary>
[PublicAPI]
public static class SetupEmailsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless email senders from a single setup builder. Provider packages contribute the
        /// default (unkeyed) sender through <c>Use*</c> extensions on <see cref="HeadlessEmailsSetupBuilder"/>
        /// (for example <c>UseAzure</c>, <c>UseAwsSes</c>, <c>UseMailkit</c>, <c>UseDevelopment</c>,
        /// <c>UseNoop</c>) and named senders through <c>setup.AddNamed(name, i =&gt; i.Use*(…))</c>. A default
        /// sender is optional (at most one); named senders are optional and unbounded. Contributions are queued
        /// and not run until the setup gates pass, so a setup that fails a gate leaves the service collection
        /// unchanged (provider <c>Use*</c> members also validate their inputs synchronously before queuing).
        /// </summary>
        /// <param name="configure">Delegate that selects the default sender and any named senders.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the delegate registers more than one default provider, configures a named instance with
        /// zero or multiple providers, reuses a name, or when <c>AddHeadlessEmails</c> has already been called
        /// on the same <see cref="IServiceCollection"/>.
        /// </exception>
        public IServiceCollection AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessEmailsSetupBuilder(services);
            configure(setup);

            return _AddEmailsProviderCore(services, setup);
        }
    }

    private static IServiceCollection _AddEmailsProviderCore(
        IServiceCollection services,
        HeadlessEmailsSetupBuilder setup
    )
    {
        if (setup.DefaultExtensions.Count > 1)
        {
            throw new InvalidOperationException(
                "Headless.Emails allows at most one default email provider. Multiple default providers were "
                    + "configured — register the additional senders as named instances with `AddNamed`."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(EmailProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessEmails was already called on this service collection. Configure all email senders "
                    + "(default and named) in a single AddHeadlessEmails call."
            );
        }

        services.AddSingleton(new EmailProviderRegistration());

        var registeredNames = setup.InstanceNames.ToFrozenSet(StringComparer.Ordinal);
        services.TryAddSingleton<IEmailSenderProvider>(provider => new KeyedServiceEmailSenderProvider(
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

    private sealed record EmailProviderRegistration;
}
