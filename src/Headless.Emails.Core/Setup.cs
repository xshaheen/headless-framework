// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering the Headless email sender.
/// </summary>
[PublicAPI]
public static class SetupEmailsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a Headless <see cref="IEmailSender"/> with the provider configured inside
        /// <paramref name="configure"/>. Exactly one <c>Use*</c> call (for example <c>UseAzure</c>,
        /// <c>UseAwsSes</c>, <c>UseMailkit</c>, <c>UseDevelopment</c>, <c>UseNoop</c>) must be made
        /// inside the delegate; zero or multiple providers throw <see cref="InvalidOperationException"/>
        /// at registration time.
        /// </summary>
        /// <param name="configure">Delegate that selects exactly one email provider.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the delegate registers zero or more than one provider, or when an email provider
        /// has already been registered on the same <see cref="IServiceCollection"/>.
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
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.Extensions.Count == 0
                    ? "Headless.Emails requires exactly one provider. Call one of `UseAzure`, `UseAwsSes`, `UseMailkit`, `UseDevelopment`, or `UseNoop`."
                    : "Headless.Emails requires exactly one provider. Multiple providers were configured."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(EmailProviderRegistration)))
        {
            throw new InvalidOperationException(
                "Headless.Emails requires exactly one provider. Multiple providers were configured."
            );
        }

        var extension = setup.Extensions.Single();
        var extensionTypeName = extension.GetType().FullName ?? "unknown";
        services.AddSingleton(new EmailProviderRegistration(extensionTypeName));

        extension.AddServices(services);

        return services;
    }

    private sealed record EmailProviderRegistration(string Provider);
}
