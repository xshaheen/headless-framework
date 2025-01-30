// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms;

[PublicAPI]
public static class AddSmsExtensions
{
    public static void AddDevSmsSender(this IServiceCollection services, string filePath)
    {
        services.AddSingleton<ISmsSender>(new DevSmsSender(filePath));
    }

    public static void AddNoopSmsSender(this IServiceCollection services)
    {
        services.AddSingleton<ISmsSender, NoopSmsSender>();
    }
}
