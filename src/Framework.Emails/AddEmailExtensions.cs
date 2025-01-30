// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Emails;

[PublicAPI]
public static class AddEmailExtensions
{
    public static IServiceCollection AddDevEmailSender(this IServiceCollection services, string filePath)
    {
        services.AddSingleton<IEmailSender>(new DevEmailSender(filePath));

        return services;
    }

    public static IServiceCollection AddNoopEmailSender(this IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, NoopEmailSender>();

        return services;
    }
}
