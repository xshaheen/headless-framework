// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Concurrency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api;

internal static class SetupEntityTagConcurrencyCore
{
    public static IServiceCollection AddHeadlessEntityTagConcurrencyCore(
        this IServiceCollection services,
        Action<EntityTagConcurrencyOptions>? configure = null
    )
    {
        services.AddOptions<EntityTagConcurrencyOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddScoped<IfMatchContext>();
        services.TryAddScoped<IIfMatchContext>(static provider => provider.GetRequiredService<IfMatchContext>());
        return services;
    }
}
