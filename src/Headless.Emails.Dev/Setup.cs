// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Registers the development or no-op email providers as the default email sender.
/// </summary>
[PublicAPI]
public static class SetupDevEmail
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Registers <see cref="DevEmailSender"/> as the default email provider, writing email content to a file.
        /// </summary>
        /// <param name="filePath">The file path where outgoing emails are recorded.</param>
        /// <returns>The builder instance.</returns>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
        public HeadlessEmailsSetupBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            setup.RegisterDefaultProvider(services =>
                services.AddSingleton<IEmailSender>(_ => new DevEmailSender(filePath))
            );

            return setup;
        }

        /// <summary>
        /// Registers <see cref="NoopEmailSender"/> as the default email provider, discarding all messages.
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
/// Registers the development or no-op email providers as a named email sender.
/// </summary>
[PublicAPI]
public static class SetupDevEmailNamed
{
    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Registers <see cref="DevEmailSender"/> as the provider for this named instance, writing email content to a file.
        /// </summary>
        /// <param name="filePath">The file path where outgoing emails are recorded.</param>
        /// <returns>The instance builder instance.</returns>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
        public HeadlessEmailInstanceBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                services.AddKeyedSingleton<IEmailSender>(name, (_, _) => new DevEmailSender(filePath))
            );

            return instance;
        }

        /// <summary>
        /// Registers <see cref="NoopEmailSender"/> as the provider for this named instance, discarding all messages.
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
