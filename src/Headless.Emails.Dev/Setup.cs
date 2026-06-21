// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails.Dev;

/// <summary>
/// Registers development and no-op email senders with the DI container.
/// </summary>
[PublicAPI]
public static class SetupDevEmail
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="DevEmailSender"/> as the <see cref="IEmailSender"/> singleton,
        /// which appends email content to a local file instead of sending to real recipients.
        /// </summary>
        /// <param name="filePath">
        /// Absolute or relative path to the file where outgoing emails are recorded.
        /// The file is created if it does not exist; existing content is preserved and
        /// new entries are appended.
        /// </param>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <paramref name="filePath"/> is <see langword="null"/> or empty.
        /// </exception>
        public IServiceCollection AddDevEmailSender(string filePath)
        {
            services.AddSingleton<IEmailSender>(new DevEmailSender(filePath));

            return services;
        }

        /// <summary>
        /// Registers <see cref="NoopEmailSender"/> as the <see cref="IEmailSender"/> singleton,
        /// which silently discards every email without sending or logging it.
        /// </summary>
        /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
        public IServiceCollection AddNoopEmailSender()
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();

            return services;
        }
    }
}
