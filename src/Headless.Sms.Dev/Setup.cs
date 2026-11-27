// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Headless.Sms.Dev;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Sms;

/// <summary>
/// Registers the development (capturing) or no-op SMS providers as the default (unkeyed) SMS sender on
/// <see cref="HeadlessSmsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevSms
{
    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>
        /// Registers the development sender as the default provider with default options. It captures each
        /// message in <see cref="ISmsCaptureStore"/> and writes no file.
        /// </summary>
        /// <returns>The builder instance.</returns>
        public HeadlessSmsSetupBuilder UseDevelopment()
        {
            return setup.UseDevelopment(static _ => { });
        }

        /// <summary>
        /// Registers the development sender as the default SMS provider. It captures each message in
        /// <see cref="ISmsCaptureStore"/> and appends a plain-text copy to <paramref name="filePath"/>, instead of
        /// sending to real phones.
        /// </summary>
        /// <param name="filePath">
        /// The absolute or relative path to the file where outgoing messages are appended. The file is created
        /// when it does not exist; existing content is preserved and new entries are appended.
        /// </param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
        public HeadlessSmsSetupBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            return setup.UseDevelopment(options => options.FilePath = filePath);
        }

        /// <summary>
        /// Registers the development sender as the default SMS provider, binding <see cref="DevSmsOptions"/>
        /// from the supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="DevSmsOptions"/> properties.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseDevelopment(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                DevSmsRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevSmsOptions, DevSmsOptionsValidator>(config, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers the development sender as the default SMS provider, configuring
        /// <see cref="DevSmsOptions"/> through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that populates the options.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseDevelopment(Action<DevSmsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                DevSmsRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevSmsOptions, DevSmsOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers the development sender as the default SMS provider, configuring
        /// <see cref="DevSmsOptions"/> through a delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">A delegate that populates the options using dependency-injection-resolved services.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseDevelopment(Action<DevSmsOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                DevSmsRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevSmsOptions, DevSmsOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Registers <see cref="NoopSmsSender"/> as the default SMS provider, which silently discards every
        /// message without sending, capturing, or logging it.
        /// </summary>
        /// <returns>The builder instance.</returns>
        public HeadlessSmsSetupBuilder UseNoop()
        {
            setup.RegisterDefaultProvider(static services =>
            {
                services.AddSingleton<ISmsSender, NoopSmsSender>();
                services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());
            });

            return setup;
        }
    }
}

/// <summary>
/// Registers the development (capturing) or no-op SMS providers as a named SMS sender on
/// <see cref="HeadlessSmsInstanceBuilder"/>. The instance resolves as a keyed <see cref="ISmsSender"/>.
/// </summary>
[PublicAPI]
public static class SetupDevSmsNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>
        /// Registers the development sender for this named instance with default options. It captures each
        /// message in <see cref="ISmsCaptureStore"/> under the instance name and writes no file.
        /// </summary>
        /// <returns>The instance builder instance.</returns>
        public HeadlessSmsInstanceBuilder UseDevelopment()
        {
            return instance.UseDevelopment(static _ => { });
        }

        /// <summary>
        /// Registers the development sender for this named instance. It captures each message in
        /// <see cref="ISmsCaptureStore"/> under the instance name and appends a plain-text copy to
        /// <paramref name="filePath"/>, instead of sending to real phones.
        /// </summary>
        /// <param name="filePath">
        /// The absolute or relative path to the file where outgoing messages are appended. The file is created
        /// when it does not exist; existing content is preserved and new entries are appended.
        /// </param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
        public HeadlessSmsInstanceBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            return instance.UseDevelopment(options => options.FilePath = filePath);
        }

        /// <summary>
        /// Registers the development sender for this named instance, binding <see cref="DevSmsOptions"/> from the
        /// supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="DevSmsOptions"/> properties.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseDevelopment(IConfiguration config)
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevSmsRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevSmsOptions, DevSmsOptionsValidator>(config, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers the development sender for this named instance, configuring <see cref="DevSmsOptions"/>
        /// through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that populates the options.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseDevelopment(Action<DevSmsOptions> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevSmsRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevSmsOptions, DevSmsOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers the development sender for this named instance, configuring <see cref="DevSmsOptions"/>
        /// through a delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">A delegate that populates the options using dependency-injection-resolved services.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseDevelopment(Action<DevSmsOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevSmsRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevSmsOptions, DevSmsOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Registers <see cref="NoopSmsSender"/> as the provider for this named instance, silently discarding
        /// every message.
        /// </summary>
        /// <returns>The instance builder instance.</returns>
        public HeadlessSmsInstanceBuilder UseNoop()
        {
            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.AddKeyedSingleton<ISmsSender, NoopSmsSender>(name);
                services.AddKeyedSingleton<IBulkSmsSender>(
                    name,
                    (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
                );
            });

            return instance;
        }
    }
}

/// <summary>
/// Marks one development sender registration, so the shared capture store can size itself from every sender's
/// options. A <see langword="null"/> <see cref="Name"/> is the default (unkeyed) sender.
/// </summary>
internal sealed record DevSmsRegistration(string? Name)
{
    public static void AddCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions
    )
    {
        configureOptions(services, name);
        services.AddSingleton(new DevSmsRegistration(name));

        services.TryAddSingleton(static sp =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<DevSmsOptions>>();
            var capacity = sp.GetServices<DevSmsRegistration>().Max(r => monitor.Get(r.Name).CaptureCapacity);

            return new SmsCaptureStore(capacity, sp.GetService<TimeProvider>() ?? TimeProvider.System);
        });

        services.TryAddSingleton<ISmsCaptureStore>(static sp => sp.GetRequiredService<SmsCaptureStore>());

        if (name is null)
        {
            services.AddSingleton<ISmsSender>(static sp => _CreateSender(sp, name: null));
            services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());

            return;
        }

        services.AddKeyedSingleton<ISmsSender>(name, (sp, _) => _CreateSender(sp, name));
        services.AddKeyedSingleton<IBulkSmsSender>(
            name,
            (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
        );
    }

    private static DevSmsSender _CreateSender(IServiceProvider sp, string? name)
    {
        return new DevSmsSender(
            sp.GetRequiredService<IOptionsMonitor<DevSmsOptions>>().Get(name),
            sp.GetRequiredService<SmsCaptureStore>(),
            name ?? string.Empty,
            sp.GetService<ICorrelationIdProvider>()
        );
    }
}
