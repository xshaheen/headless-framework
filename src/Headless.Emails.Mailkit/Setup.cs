// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

/// <summary>
/// Extension members for selecting MailKit/SMTP as a Headless email provider — the default (unkeyed) sender
/// on <see cref="HeadlessEmailsSetupBuilder"/> or a named sender on <see cref="HeadlessEmailInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupMailkit
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Selects MailKit/SMTP as the default email provider, binding <see cref="MailkitSmtpOptions"/> from
        /// the supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="MailkitSmtpOptions"/> properties.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                _AddMailkit(services, s => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config))
            );

            return setup;
        }

        /// <summary>
        /// Selects MailKit/SMTP as the default email provider, configuring <see cref="MailkitSmtpOptions"/> via
        /// a setup delegate.
        /// </summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(Action<MailkitSmtpOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                _AddMailkit(services, s => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure))
            );

            return setup;
        }

        /// <summary>
        /// Selects MailKit/SMTP as the default email provider, configuring <see cref="MailkitSmtpOptions"/> via
        /// a setup delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that populates the options using DI-resolved services.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                _AddMailkit(services, s => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure))
            );

            return setup;
        }
    }

    private static void _AddMailkit(IServiceCollection services, Action<IServiceCollection> configureOptions)
    {
        configureOptions(services);

        services.AddSingleton<IPooledObjectPolicy<SmtpClient>, SmtpClientPooledObjectPolicy>();
        services.AddSingleton<ObjectPool<SmtpClient>>(static sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MailkitSmtpOptions>>().Value;
            var policy = sp.GetRequiredService<IPooledObjectPolicy<SmtpClient>>();
            var provider = new DefaultObjectPoolProvider { MaximumRetained = opts.MaxPoolSize };

            return provider.Create(policy);
        });
        services.AddSingleton<IEmailSender, MailkitEmailSender>();
    }
}
