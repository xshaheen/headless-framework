// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Emails.Mailkit;

[PublicAPI]
public static class AddMailKitExtensions
{
    public static IServiceCollection AddMailKitEmailSender(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureSingleton<MailkitSmtpSettings, MailkitSmtpSettingsValidator>(config);

        return _AddCore(services);
    }

    public static IServiceCollection AddMailKitEmailSender(
        this IServiceCollection services,
        Action<MailkitSmtpSettings> configure
    )
    {
        services.ConfigureSingleton<MailkitSmtpSettings, MailkitSmtpSettingsValidator>(configure);
        services.AddSingleton<IEmailSender, MailkitEmailSender>();

        return _AddCore(services);
    }

    public static IServiceCollection AddMailKitEmailSender(
        this IServiceCollection services,
        Action<MailkitSmtpSettings, IServiceProvider> configure
    )
    {
        services.ConfigureSingleton<MailkitSmtpSettings, MailkitSmtpSettingsValidator>(configure);
        services.AddSingleton<IEmailSender, MailkitEmailSender>();

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, MailkitEmailSender>();

        return services;
    }
}
