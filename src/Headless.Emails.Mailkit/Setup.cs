// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

/// <summary>
/// Extension members on <see cref="HeadlessEmailsSetupBuilder"/> for selecting MailKit/SMTP as the
/// email provider.
/// </summary>
[PublicAPI]
public static class SetupMailkit
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Selects MailKit/SMTP as the email provider, binding <see cref="MailkitSmtpOptions"/> from the
        /// supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="MailkitSmtpOptions"/> properties.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterExtension(new MailkitEmailOptionsExtension(config));

            return setup;
        }

        /// <summary>
        /// Selects MailKit/SMTP as the email provider, configuring <see cref="MailkitSmtpOptions"/> via a
        /// setup delegate.
        /// </summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(Action<MailkitSmtpOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new MailkitEmailOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Selects MailKit/SMTP as the email provider, configuring <see cref="MailkitSmtpOptions"/> via a
        /// setup delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that populates the options using DI-resolved services.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new MailkitEmailOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class MailkitEmailOptionsExtension : IEmailProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configure;

        public MailkitEmailOptionsExtension(IConfiguration config)
        {
            _configure = services => services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config);
        }

        public MailkitEmailOptionsExtension(Action<MailkitSmtpOptions> configure)
        {
            _configure = services => services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure);
        }

        public MailkitEmailOptionsExtension(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            _configure = services => services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure);
        }

        public void AddServices(IServiceCollection services)
        {
            _configure(services);

            services.AddSingleton<IPooledObjectPolicy<SmtpClient>, SmtpClientPooledObjectPolicy>();
            services.AddSingleton<ObjectPool<SmtpClient>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<MailkitSmtpOptions>>().Value;
                var policy = sp.GetRequiredService<IPooledObjectPolicy<SmtpClient>>();
                var provider = new DefaultObjectPoolProvider { MaximumRetained = opts.MaxPoolSize };
                return provider.Create(policy);
            });
            services.AddSingleton<IEmailSender, MailkitEmailSender>();
        }
    }
}
