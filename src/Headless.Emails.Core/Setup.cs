// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Emails;

/// <summary>
/// Registers Headless email services with the dependency injection container.
/// </summary>
[PublicAPI]
public static class SetupEmailsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers email senders and providers with the dependency injection container.
        /// </summary>
        /// <param name="configure">A delegate that configures email providers.</param>
        /// <returns>The service collection instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Multiple default providers are registered, a named instance has invalid provider configuration, a name is reused, or email services are already registered on the collection.
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
