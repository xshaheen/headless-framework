// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
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
                _AddEmailsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config, n)
                )
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
                _AddEmailsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
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
                _AddEmailsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return setup;
        }
    }

    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Uses MailKit/SMTP for this named instance, binding <see cref="MailkitSmtpOptions"/> from the supplied
        /// configuration section. The instance owns its own keyed SMTP client pool, pool policy, and named
        /// options; it never shares them with the default sender or other named instances.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="MailkitSmtpOptions"/> properties.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseMailkit(IConfiguration config)
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                _AddEmailsCore(
                    services,
                    name,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Uses MailKit/SMTP for this named instance, configuring <see cref="MailkitSmtpOptions"/> via a setup
        /// delegate.
        /// </summary>
        /// <param name="configure">Delegate that populates the options.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseMailkit(Action<MailkitSmtpOptions> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                _AddEmailsCore(
                    services,
                    name,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Uses MailKit/SMTP for this named instance, configuring <see cref="MailkitSmtpOptions"/> via a setup
        /// delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that populates the options using DI-resolved services.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseMailkit(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                _AddEmailsCore(
                    services,
                    name,
                    (s, n) => s.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure, n)
                )
            );

            return instance;
        }
    }

    /// <summary>
    /// Registers the MailKit email sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) pool, policy, and sender; a non-null name registers a keyed pool, policy, and sender plus named
    /// options. Every factory reads the options snapshot for its own name (<c>IOptionsMonitor.Get(name)</c>) so
    /// keyed SMTP settings never bleed across instances — keyed DI does not cascade the key to ctor
    /// dependencies, and a keyed sender/policy must not read <c>CurrentValue</c> (which binds the default).
    /// </summary>
    private static void _AddEmailsCore(
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
