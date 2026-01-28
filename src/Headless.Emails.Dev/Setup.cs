// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails.Dev;

[PublicAPI]
public static class DevEmailSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDevEmailSender(string filePath)
        {
            services.AddSingleton<IEmailSender>(new DevEmailSender(filePath));

            return services;
        }

        public IServiceCollection AddNoopEmailSender()
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();

            return services;
        }
    }
}
