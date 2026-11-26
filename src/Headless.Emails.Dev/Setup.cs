// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Headless.Emails.Dev;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Emails;

/// <summary>
/// Registers the development (capturing) or no-op email providers as the default (unkeyed) email sender on
/// <see cref="HeadlessEmailsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevEmail
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Registers the development sender as the default provider with default options. It captures each
        /// email in <see cref="IEmailCaptureStore"/> and writes no file.
        /// </summary>
        /// <returns>The builder instance.</returns>
        public HeadlessEmailsSetupBuilder UseDevelopment()
        {
            return setup.UseDevelopment(static _ => { });
        }

        /// <summary>
        /// Registers the development sender as the default email provider. It captures each email in
        /// <see cref="IEmailCaptureStore"/> and appends a plain-text copy to <paramref name="filePath"/>, instead of
        /// sending to real recipients.
        /// </summary>
        /// <param name="filePath">
        /// The absolute or relative path to the file where outgoing emails are recorded. The file is created
        /// when it does not exist; existing content is preserved and new entries are appended.
        /// </param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
        public HeadlessEmailsSetupBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            return setup.UseDevelopment(options => options.FilePath = filePath);
        }

        /// <summary>
        /// Registers the development sender as the default email provider, binding <see cref="DevEmailOptions"/>
        /// from the supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="DevEmailOptions"/> properties.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseDevelopment(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                DevEmailRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevEmailOptions, DevEmailOptionsValidator>(config, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers the development sender as the default email provider, configuring
        /// <see cref="DevEmailOptions"/> through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that populates the options.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseDevelopment(Action<DevEmailOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                DevEmailRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevEmailOptions, DevEmailOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers the development sender as the default email provider, configuring
        /// <see cref="DevEmailOptions"/> through a delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">A delegate that populates the options using dependency-injection-resolved services.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailsSetupBuilder UseDevelopment(Action<DevEmailOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                DevEmailRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevEmailOptions, DevEmailOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers <see cref="NoopEmailSender"/> as the default email provider, which silently discards every
        /// email without sending, capturing, or logging it.
        /// </summary>
        /// <returns>The builder instance.</returns>
        public HeadlessEmailsSetupBuilder UseNoop()
        {
            setup.RegisterDefaultProvider(static services => services.AddSingleton<IEmailSender, NoopEmailSender>());

            return setup;
        }
    }
}

/// <summary>
/// Registers the development (capturing) or no-op email providers as a named email sender on
/// <see cref="HeadlessEmailInstanceBuilder"/>. The instance resolves as a keyed <see cref="IEmailSender"/>.
/// </summary>
[PublicAPI]
public static class SetupDevEmailNamed
{
    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Registers the development sender for this named instance with default options. It captures each
        /// email in <see cref="IEmailCaptureStore"/> under the instance name and writes no file.
        /// </summary>
        /// <returns>The instance builder instance.</returns>
        public HeadlessEmailInstanceBuilder UseDevelopment()
        {
            return instance.UseDevelopment(static _ => { });
        }

        /// <summary>
        /// Registers the development sender for this named instance. It captures each email in
        /// <see cref="IEmailCaptureStore"/> under the instance name and appends a plain-text copy to
        /// <paramref name="filePath"/>, instead of sending to real recipients.
        /// </summary>
        /// <param name="filePath">
        /// The absolute or relative path to the file where outgoing emails are recorded. The file is created
        /// when it does not exist; existing content is preserved and new entries are appended.
        /// </param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
        public HeadlessEmailInstanceBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            return instance.UseDevelopment(options => options.FilePath = filePath);
        }

        /// <summary>
        /// Registers the development sender for this named instance, binding <see cref="DevEmailOptions"/> from the
        /// supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="DevEmailOptions"/> properties.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseDevelopment(IConfiguration config)
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevEmailRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevEmailOptions, DevEmailOptionsValidator>(config, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers the development sender for this named instance, configuring <see cref="DevEmailOptions"/>
        /// through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that populates the options.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseDevelopment(Action<DevEmailOptions> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevEmailRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevEmailOptions, DevEmailOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers the development sender for this named instance, configuring <see cref="DevEmailOptions"/>
        /// through a delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">A delegate that populates the options using dependency-injection-resolved services.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessEmailInstanceBuilder UseDevelopment(Action<DevEmailOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevEmailRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevEmailOptions, DevEmailOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers <see cref="NoopEmailSender"/> as the provider for this named instance, silently discarding
        /// every email.
        /// </summary>
        /// <returns>The instance builder instance.</returns>
        public HeadlessEmailInstanceBuilder UseNoop()
        {
            var name = instance.Name;

            instance.RegisterProvider(services => services.AddKeyedSingleton<IEmailSender, NoopEmailSender>(name));

            return instance;
        }
    }
}

/// <summary>
/// Marks one development sender registration, so the shared capture store can size itself from every sender's
/// options. A <see langword="null"/> <see cref="Name"/> is the default (unkeyed) sender.
/// </summary>
internal sealed record DevEmailRegistration(string? Name)
{
    public static void AddCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions
    )
    {
        configureOptions(services, name);
        services.AddSingleton(new DevEmailRegistration(name));

        services.TryAddSingleton(static sp =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<DevEmailOptions>>();
            var capacity = sp.GetServices<DevEmailRegistration>().Max(r => monitor.Get(r.Name).CaptureCapacity);

            return new EmailCaptureStore(capacity, sp.GetService<TimeProvider>() ?? TimeProvider.System);
        });

        services.TryAddSingleton<IEmailCaptureStore>(static sp => sp.GetRequiredService<EmailCaptureStore>());

        if (name is null)
        {
            services.AddSingleton<IEmailSender>(static sp => _CreateSender(sp, name: null));

            return;
        }

        services.AddKeyedSingleton<IEmailSender>(name, (sp, _) => _CreateSender(sp, name));
    }

    private static DevEmailSender _CreateSender(IServiceProvider sp, string? name)
    {
        return new DevEmailSender(
            sp.GetRequiredService<IOptionsMonitor<DevEmailOptions>>().Get(name),
            sp.GetRequiredService<EmailCaptureStore>(),
            name ?? string.Empty,
            sp.GetService<ICorrelationIdProvider>()
        );
    }
}
