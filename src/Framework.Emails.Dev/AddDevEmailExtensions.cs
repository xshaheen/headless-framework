// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Framework.Emails.Dev;

[PublicAPI]
public static class AddDevEmailExtensions
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
