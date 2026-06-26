// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Emails.Dev;

/// <summary>
/// Extension members for selecting the development (file-writing) or no-op email providers — the default
/// (unkeyed) sender on <see cref="HeadlessEmailsSetupBuilder"/> or a named sender on
/// <see cref="HeadlessEmailInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevEmail
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Selects <see cref="DevEmailSender"/> as the default email provider, which appends email content to
        /// a local file instead of sending to real recipients.
        /// </summary>
        /// <param name="filePath">
        /// Absolute or relative path to the file where outgoing emails are recorded.
        /// The file is created if it does not exist; existing content is preserved and
        /// new entries are appended.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="filePath"/> is <see langword="null"/> or empty.
        /// </exception>
        public HeadlessEmailsSetupBuilder UseDevelopment(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);

            setup.RegisterDefaultProvider(services =>
                services.AddSingleton<IEmailSender>(_ => new DevEmailSender(filePath))
            );

            return setup;
        }

        /// <summary>
        /// Selects <see cref="NoopEmailSender"/> as the default email provider, which silently discards every
        /// email without sending or logging it.
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
        public HeadlessEmailsSetupBuilder UseNoop()
        {
            setup.RegisterDefaultProvider(static services => services.AddSingleton<IEmailSender, NoopEmailSender>());

            return setup;
        }
    }

    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Uses <see cref="DevEmailSender"/> for this named instance, appending email content to a local file
        /// instead of sending to real recipients. The instance resolves as a keyed <see cref="IEmailSender"/>.
        /// </summary>
        /// <param name="filePath">
        /// Absolute or relative path to the file where outgoing emails are recorded. The file is created if it
        /// does not exist; existing content is preserved and new entries are appended.
        /// </param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="filePath"/> is <see langword="null"/> or empty.
        /// </exception>
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
        /// Uses <see cref="NoopEmailSender"/> for this named instance, silently discarding every email. The
        /// instance resolves as a keyed <see cref="IEmailSender"/>.
        /// </summary>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessEmailInstanceBuilder UseNoop()
        {
            var name = instance.Name;

            instance.RegisterProvider(services => services.AddKeyedSingleton<IEmailSender, NoopEmailSender>(name));

            return instance;
        }
    }
}
