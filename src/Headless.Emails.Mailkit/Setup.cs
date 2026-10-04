// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Emails.Mailkit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails;

/// <summary>
/// Registers MailKit SMTP as the default email provider on <see cref="HeadlessEmailsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupMailkit
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Registers MailKit SMTP as the default email provider, binding options from configuration.
        /// </summary>
        /// <param name="config">The configuration section.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                AddEmailsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers MailKit SMTP as the default email provider, configuring options through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that configures options.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(Action<MailkitSmtpOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                AddEmailsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers MailKit SMTP as the default email provider, configuring options with access to service providers.
        /// </summary>
        /// <param name="configure">A delegate that configures options using resolved services.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseMailkit(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                AddEmailsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// Registers the MailKit email sender.
    /// </summary>
    internal static void AddEmailsCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions
    )
    {
        configureOptions(services, name);

        if (name is null)
        {
            services.AddSingleton<IPooledObjectPolicy<SmtpClient>>(static sp => new SmtpClientPooledObjectPolicy(
                sp.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>(),
                optionsName: null
            ));

            services.AddSingleton<ObjectPool<SmtpClient>>(static sp =>
            {
                var maxPoolSize = sp.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>()
                    .Get(name: null)
                    .MaxPoolSize;
                var policy = sp.GetRequiredService<IPooledObjectPolicy<SmtpClient>>();

                return new DefaultObjectPoolProvider { MaximumRetained = maxPoolSize }.Create(policy);
            });

            services.AddSingleton<IEmailSender>(static sp => new MailkitEmailSender(
                sp.GetRequiredService<ObjectPool<SmtpClient>>(),
                sp.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>(),
                optionsName: null,
                sp.GetRequiredService<ILogger<MailkitEmailSender>>()
            ));

            return;
        }

        services.AddKeyedSingleton<IPooledObjectPolicy<SmtpClient>>(
            name,
            (sp, _) =>
                new SmtpClientPooledObjectPolicy(sp.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>(), name)
        );

        services.AddKeyedSingleton<ObjectPool<SmtpClient>>(
            name,
            (sp, _) =>
            {
                var maxPoolSize = sp.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>().Get(name).MaxPoolSize;
                var policy = sp.GetRequiredKeyedService<IPooledObjectPolicy<SmtpClient>>(name);

                return new DefaultObjectPoolProvider { MaximumRetained = maxPoolSize }.Create(policy);
            }
        );

        services.AddKeyedSingleton<IEmailSender>(
            name,
            (sp, _) =>
                new MailkitEmailSender(
                    sp.GetRequiredKeyedService<ObjectPool<SmtpClient>>(name),
                    sp.GetRequiredService<IOptionsMonitor<MailkitSmtpOptions>>(),
                    name,
                    sp.GetRequiredService<ILogger<MailkitEmailSender>>()
                )
        );
    }
}

/// <summary>
/// Registers MailKit SMTP as a named email sender on <see cref="HeadlessEmailInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupMailkitNamed
{
    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Registers MailKit SMTP for this named instance, binding options from configuration.
        /// </summary>
        /// <param name="config">The configuration section.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseMailkit(IConfiguration config)
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupMailkit.AddEmailsCore(
                    services,
                    name,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers MailKit SMTP for this named instance, configuring options through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that configures options.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseMailkit(Action<MailkitSmtpOptions> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupMailkit.AddEmailsCore(
                    services,
                    name,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers MailKit SMTP for this named instance, configuring options with access to service providers.
        /// </summary>
        /// <param name="configure">A delegate that configures options using resolved services.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseMailkit(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupMailkit.AddEmailsCore(
                    services,
                    name,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return instance;
        }
    }
}
