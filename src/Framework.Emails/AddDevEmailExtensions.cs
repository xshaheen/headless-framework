// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Emails.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Emails;

[PublicAPI]
public static class AddDevEmailExtensions
{
    public static IServiceCollection AddDevEmailSender(this IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, DevEmailSender>();

        return services;
    }

    public static IServiceCollection AddNoopEmailSender(this IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, NoopEmailSender>();

        return services;
    }
}
