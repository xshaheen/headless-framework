// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Registers the development (file-writing) or no-op email providers as the default (unkeyed) email sender
/// on <see cref="HeadlessEmailsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevEmail
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Registers <see cref="DevEmailSender"/> as the default email provider, which appends email content to
        /// a local file instead of sending to real recipients.
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

            setup.RegisterDefaultProvider(services =>
                services.AddSingleton<IEmailSender>(_ => new DevEmailSender(filePath))
            );

            return setup;
        }

        /// <summary>
        /// Registers <see cref="NoopEmailSender"/> as the default email provider, which silently discards every
        /// email without sending or logging it.
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
/// Registers the development (file-writing) or no-op email providers as a named email sender on
/// <see cref="HeadlessEmailInstanceBuilder"/>. The instance resolves as a keyed <see cref="IEmailSender"/>.
/// </summary>
[PublicAPI]
public static class SetupDevEmailNamed
{
    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Registers <see cref="DevEmailSender"/> as the provider for this named instance, appending email
        /// content to a local file instead of sending to real recipients.
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

            var name = instance.Name;

            instance.RegisterProvider(services =>
                services.AddKeyedSingleton<IEmailSender>(name, (_, _) => new DevEmailSender(filePath))
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
