// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms.Twilio;

[PublicAPI]
public static class AddTwilioExtensions
{
    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        Action<TwilioSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<TwilioSettings, TwilioSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddTwilioSmsSender(
        this IServiceCollection services,
        Action<TwilioSettings> setupAction
    )
    {
        services.ConfigureSingleton<TwilioSettings, TwilioSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<ISmsSender, TwilioSmsSender>();

        return services;
    }
}
