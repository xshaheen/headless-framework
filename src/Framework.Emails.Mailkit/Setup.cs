// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Emails.Mailkit;

[PublicAPI]
public static class MailkitSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMailKitEmailSender(IConfiguration config)
        {
            services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(config);

            return _AddCore(services);
        }

        public IServiceCollection AddMailKitEmailSender(Action<MailkitSmtpOptions> configure)
        {
            services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure);

            return _AddCore(services);
        }

        public IServiceCollection AddMailKitEmailSender(Action<MailkitSmtpOptions, IServiceProvider> configure)
        {
            services.Configure<MailkitSmtpOptions, MailkitSmtpOptionsValidator>(configure);

            return _AddCore(services);
        }
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, MailkitEmailSender>();

        return services;
    }
}
