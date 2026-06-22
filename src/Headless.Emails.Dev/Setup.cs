// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails.Dev;

/// <summary>
/// Extension members on <see cref="HeadlessEmailsSetupBuilder"/> for selecting the development
/// (file-writing) or no-op email providers.
/// </summary>
[PublicAPI]
public static class SetupDevEmail
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Selects <see cref="DevEmailSender"/> as the email provider, which appends email content to a
        /// local file instead of sending to real recipients.
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

            setup.RegisterExtension(new DevEmailOptionsExtension(filePath));

            return setup;
        }

        /// <summary>
        /// Selects <see cref="NoopEmailSender"/> as the email provider, which silently discards every
        /// email without sending or logging it.
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
        public HeadlessEmailsSetupBuilder UseNoop()
        {
            setup.RegisterExtension(new NoopEmailOptionsExtension());

            return setup;
        }
    }

    private sealed class DevEmailOptionsExtension(string filePath) : IEmailProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<IEmailSender>(new DevEmailSender(filePath));
        }
    }

    private sealed class NoopEmailOptionsExtension : IEmailProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();
        }
    }
}
