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
        /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
        public HeadlessSmsSetupBuilder UseDev(string filePath)
        {
            Argument.IsNotNullOrEmpty(filePath);
            setup.RegisterExtension(new DevProviderOptionsExtension(filePath));

            return setup;
        }

        /// <summary>Selects the no-op sender, which discards every message and reports success.</summary>
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
