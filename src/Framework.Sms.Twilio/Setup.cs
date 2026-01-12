// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms.Twilio;

[PublicAPI]
public static class TwilioSetup
{
    public static IServiceCollection AddTwilioSmsSender(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(config);
        return _AddCore(services);
    }

    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        Action<TwilioSmsOptions, IServiceProvider> setupAction
    )
    {
        services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        Action<TwilioSmsOptions> setupAction
    )
    {
        services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<ISmsSender, TwilioSmsSender>();

        return services;
    }
}
