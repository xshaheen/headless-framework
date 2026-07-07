// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>
/// Extension members for selecting the development (file-writing) or no-op SMS providers as the default
/// (unkeyed) sender on <see cref="HeadlessSmsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevSms
{
    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects the development sender, which appends each message to <paramref name="filePath"/>.</summary>
        /// <remarks>No real SMS is sent. Messages are written to the file in plaintext for local inspection.</remarks>
        /// <param name="filePath">Absolute or relative path to the file where outgoing messages are appended.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is an empty string.</exception>
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

        /// <summary>Selects the no-op sender, which discards every message and always reports success.</summary>
        /// <remarks>No real SMS is sent and no file is written. Useful in test environments where SMS delivery is irrelevant.</remarks>
        /// <returns>The same builder for chaining.</returns>
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
/// Extension members for selecting the development (file-writing) or no-op SMS providers for a named
/// instance on <see cref="HeadlessSmsInstanceBuilder"/>. The instance resolves as a keyed
/// <see cref="ISmsSender"/> (and keyed <see cref="IBulkSmsSender"/>) or through
/// <see cref="ISmsSenderProvider"/>.
/// </summary>
[PublicAPI]
public static class SetupDevSmsNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>Uses the development sender for this named instance, appending each message to <paramref name="filePath"/>.</summary>
        /// <remarks>No real SMS is sent. Messages are written to the file in plaintext for local inspection.</remarks>
        /// <param name="filePath">Absolute or relative path to the file where outgoing messages are appended.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is an empty string.</exception>
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

        /// <summary>Uses the no-op sender for this named instance, discarding every message and always reporting success.</summary>
        /// <remarks>No real SMS is sent and no file is written. Useful in test environments where SMS delivery is irrelevant.</remarks>
        /// <returns>The instance builder for chaining.</returns>
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
