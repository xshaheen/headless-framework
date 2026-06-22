// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

/// <summary>
/// Registers the MailKit SMTP email sender with the DI container.
/// </summary>
[PublicAPI]
public static class SetupMailkit
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="MailkitEmailSender"/> as the <see cref="IEmailSender"/> singleton,
        /// binding <see cref="MailkitSmtpOptions"/> from the supplied configuration section.
        /// </summary>
        /// <param name="config">
        /// The configuration section that maps to <see cref="MailkitSmtpOptions"/> properties.
        /// </param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        public IServiceCollection AddMailKitEmailSender(IConfiguration config)
        {
            services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config);
            return _AddCore(services);
        }

        /// <summary>
        /// Registers <see cref="MailkitEmailSender"/> as the <see cref="IEmailSender"/> singleton,
        /// configuring <see cref="MailkitSmtpOptions"/> via a setup delegate.
        /// </summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        public IServiceCollection AddMailKitEmailSender(Action<MailkitSmtpOptions> configure)
        {
            services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure);
            return _AddCore(services);
        }

        /// <summary>
        /// Registers <see cref="MailkitEmailSender"/> as the <see cref="IEmailSender"/> singleton,
        /// configuring <see cref="MailkitSmtpOptions"/> via a setup delegate that also receives
        /// the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that populates the options using DI-resolved services.</param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        public IServiceCollection AddMailKitEmailSender(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure);
            return _AddCore(services);
        }
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IPooledObjectPolicy<SmtpClient>, SmtpClientPooledObjectPolicy>();
        services.AddSingleton<ObjectPool<SmtpClient>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MailkitSmtpOptions>>().Value;
            var policy = sp.GetRequiredService<IPooledObjectPolicy<SmtpClient>>();
            var provider = new DefaultObjectPoolProvider { MaximumRetained = opts.MaxPoolSize };
            return provider.Create(policy);
        });
        services.AddSingleton<IEmailSender, MailkitEmailSender>();

        return services;
    }
}
