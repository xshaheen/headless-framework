// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms;

/// <summary>
/// Provides extension methods for registering development and no-op SMS providers on <see cref="HeadlessSmsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevSms
{
    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Registers the development SMS sender, appending outgoing messages to the specified file.</summary>
        /// <remarks>Outgoing messages are saved as plain text for local inspection and are not transmitted.</remarks>
        /// <param name="filePath">The file path where outgoing messages are appended.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
        public HeadlessSmsSetupBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            setup.RegisterDefaultProvider(services =>
            {
                services.AddSingleton<ISmsSender>(_ => new DevSmsSender(filePath));
                services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());
            });

            return setup;
        }

        /// <summary>Registers the no-op SMS sender, discarding outgoing messages and returning success.</summary>
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
/// Provides extension methods for configuring named development and no-op SMS instances on <see cref="HeadlessSmsInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevSmsNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>Configures the development SMS sender for this named instance.</summary>
        /// <remarks>Outgoing messages are saved as plain text for local inspection and are not transmitted.</remarks>
        /// <param name="filePath">The file path where outgoing messages are appended.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
        public HeadlessSmsInstanceBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.AddKeyedSingleton<ISmsSender>(name, (_, _) => new DevSmsSender(filePath));
                services.AddKeyedSingleton<IBulkSmsSender>(
                    name,
                    (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
                );
            });

            return instance;
        }

        /// <summary>Configures the no-op SMS sender for this named instance.</summary>
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
