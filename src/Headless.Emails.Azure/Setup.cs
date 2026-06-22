// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Azure;
using Azure.Communication.Email;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Emails.Azure;

/// <summary>
/// Extension members on <see cref="HeadlessEmailsSetupBuilder"/> for selecting Azure Communication
/// Services (ACS) Email as the email provider.
/// </summary>
[PublicAPI]
public static class SetupAzureEmail
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Selects Azure Communication Services Email as the email provider, binding
        /// <see cref="AzureCommunicationEmailOptions"/> from the supplied configuration section.
        /// </summary>
        /// <param name="configuration">The configuration section to bind provider options from.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// Configuration binding supports only the connection-string and endpoint + access-key auth modes.
        /// Managed-identity (<see cref="AzureCommunicationEmailOptions.TokenCredential"/>) auth requires a
        /// delegate overload, since a credential cannot be bound from configuration.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseAzure(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new AzureCommunicationEmailOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Selects Azure Communication Services Email as the email provider, configuring
        /// <see cref="AzureCommunicationEmailOptions"/> via a setup delegate.
        /// </summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseAzure(Action<AzureCommunicationEmailOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new AzureCommunicationEmailOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Selects Azure Communication Services Email as the email provider, configuring
        /// <see cref="AzureCommunicationEmailOptions"/> via a setup delegate that also receives the
        /// <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that populates the options using DI-resolved services.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseAzure(Action<AzureCommunicationEmailOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new AzureCommunicationEmailOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class AzureCommunicationEmailOptionsExtension : IEmailProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configure;

        public AzureCommunicationEmailOptionsExtension(IConfiguration configuration)
        {
            _configure = services =>
                services.Configure<AzureCommunicationEmailOptions, AzureCommunicationEmailOptionsValidator>(
                    configuration
                );
        }

        public AzureCommunicationEmailOptionsExtension(Action<AzureCommunicationEmailOptions> configure)
        {
            _configure = services =>
                services.Configure<AzureCommunicationEmailOptions, AzureCommunicationEmailOptionsValidator>(configure);
        }

        public AzureCommunicationEmailOptionsExtension(
            Action<AzureCommunicationEmailOptions, IServiceProvider> configure
        )
        {
            _configure = services =>
                services.Configure<AzureCommunicationEmailOptions, AzureCommunicationEmailOptionsValidator>(configure);
        }

        public void AddServices(IServiceCollection services)
        {
            _configure(services);

            services.AddSingleton(static sp =>
            {
                var options = sp.GetRequiredService<IOptions<AzureCommunicationEmailOptions>>().Value;

                return _CreateClient(options);
            });

            services.AddSingleton<IEmailSender, AzureCommunicationEmailSender>();
        }
    }

    private static EmailClient _CreateClient(AzureCommunicationEmailOptions options)
    {
        // The options validator (wired with ValidateOnStart via Configure<TOptions, TValidator>) guarantees
        // exactly one mode before this factory runs, so the resolved mode is never Unconfigured/Ambiguous on
        // the validated path. Note: in a non-hosted container (bare BuildServiceProvider without IHost),
        // ValidateOnStart never fires, so a misconfiguration instead surfaces here on first resolve.
        return options.ResolveAuthMode() switch
        {
            AzureCommunicationEmailAuthMode.ConnectionString => new EmailClient(options.ConnectionString!),
            AzureCommunicationEmailAuthMode.TokenCredential => new EmailClient(
                options.Endpoint!,
                options.TokenCredential!
            ),
            AzureCommunicationEmailAuthMode.AccessKey => new EmailClient(
                options.Endpoint!,
                new AzureKeyCredential(options.AccessKey!)
            ),
            var mode => throw new UnreachableException(
                $"AzureCommunicationEmailOptions resolved to {mode}; the options validator should have rejected this before the EmailClient factory ran."
            ),
        };
    }
}
