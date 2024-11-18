// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sms.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms;

[PublicAPI]
public static class AddDevSmsExtensions
{
    public static void AddDevSmsSender(this IServiceCollection services)
    {
        services.AddSingleton<ISmsSender, DevSmsSender>();
    }

    public static void AddNoopSmsSender(this IServiceCollection services)
    {
        services.AddSingleton<ISmsSender, NoopSmsSender>();
    }
}
