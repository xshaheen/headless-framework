// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

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
        services.AddSingleton<IPooledObjectPolicy<SmtpClient>, SmtpClientPooledObjectPolicy>();
        services.AddSingleton<ObjectPool<SmtpClient>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MailkitSmtpOptions>>().Value;
            var policy = sp.GetRequiredService<IPooledObjectPolicy<SmtpClient>>();
            var provider = new DefaultObjectPoolProvider { MaximumRetained = opts.MaxPoolSize };
            return provider.Create(policy);
        });
        services.AddSingleton<IEmailSender, MailkitEmailSender>();

        return services;
    }
}
