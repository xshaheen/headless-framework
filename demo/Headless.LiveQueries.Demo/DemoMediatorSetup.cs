// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Mediator;

namespace Demo;

/// <summary>Demo code: registers <see cref="DemoSender"/> and every request handler of this assembly.</summary>
public static class DemoMediatorSetup
{
    public static IServiceCollection AddDemoMediator(this IServiceCollection services)
    {
        services.AddScoped<ISender, DemoSender>();

        var handlers = typeof(DemoMediatorSetup)
            .Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type =>
                type.GetInterfaces()
                    .Where(contract =>
                        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                    )
                    .Select(contract => (Contract: contract, Implementation: type))
            );

        foreach (var (contract, implementation) in handlers)
        {
            services.AddScoped(contract, implementation);
        }

        return services;
    }
}
