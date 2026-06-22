// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms.Dev;

[PublicAPI]
public static class SetupDevSms
{
    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects the development sender, which appends each message to <paramref name="filePath"/>.</summary>
        /// <remarks>No real SMS is sent. Messages are written to the file in plaintext for local inspection.</remarks>
        /// <param name="filePath">Absolute or relative path to the file where outgoing messages are appended.</param>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is an empty string.</exception>
        public HeadlessSmsSetupBuilder UseDev(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);
            setup.RegisterExtension(new DevProviderOptionsExtension(filePath));

            return setup;
        }

        /// <summary>Selects the no-op sender, which discards every message and always reports success.</summary>
        /// <remarks>No real SMS is sent and no file is written. Useful in test environments where SMS delivery is irrelevant.</remarks>
        public HeadlessSmsSetupBuilder UseNoop()
        {
            setup.RegisterExtension(new NoopProviderOptionsExtension());

            return setup;
        }
    }

    private sealed class DevProviderOptionsExtension(string filePath) : ISmsProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<ISmsSender>(_ => new DevSmsSender(filePath));
        }
    }

    private sealed class NoopProviderOptionsExtension : ISmsProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<ISmsSender, NoopSmsSender>();
        }
    }
}
