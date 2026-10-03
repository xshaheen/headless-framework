// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Registration;
using Headless.Reliability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging;

internal static class MessagingTuningRecording
{
    public static void AddConsumerTuning(
        this IServiceCollection services,
        string identity,
        Action<ConsumerTuningBuilder> configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.IsNotNull(configure);

        var builder = new ConsumerTuningBuilder(identity);
        configure(builder);
        var tuning = builder.Build();

        foreach (var middleware in tuning.Middleware)
        {
            services.TryAdd(ServiceDescriptor.Scoped(middleware, middleware));
        }

        services.AddSingleton(new MessagingTuningContribution(tuning));
    }
}
