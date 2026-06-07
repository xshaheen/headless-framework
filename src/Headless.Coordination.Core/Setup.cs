// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Coordination;

[PublicAPI]
public static class SetupCoordinationCore
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCoordinationCore<TStore>(
            Action<CoordinationOptions, IServiceProvider> optionSetupAction
        )
            where TStore : class, IMembershipStore
        {
            services.Configure<CoordinationOptions, CoordinationOptionsValidator>(optionSetupAction);

            return services._AddCoordinationCore<TStore>();
        }

        public IServiceCollection AddCoordinationCore<TStore>(Action<CoordinationOptions> optionSetupAction)
            where TStore : class, IMembershipStore
        {
            services.Configure<CoordinationOptions, CoordinationOptionsValidator>(optionSetupAction);

            return services._AddCoordinationCore<TStore>();
        }

        public IServiceCollection AddCoordinationCore<TStore>(IConfiguration configuration)
            where TStore : class, IMembershipStore
        {
            services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configuration);

            return services._AddCoordinationCore<TStore>();
        }

        private IServiceCollection _AddCoordinationCore<TStore>()
            where TStore : class, IMembershipStore
        {
            services.TryAddSingleton<TStore>();

            return services._AddCoordinationCore(static provider => provider.GetRequiredService<TStore>());
        }

        private IServiceCollection _AddCoordinationCore(Func<IServiceProvider, IMembershipStore> storeFactory)
        {
            services.AddSingletonOptionValue<CoordinationOptions>();
            services.TryAddSingleton(TimeProvider.System);
            services.AddHeadlessGuidGenerator();
            services.TryAddSingleton<INodeIdProvider, DefaultNodeIdProvider>();
            services.TryAddSingleton<MembershipEventSource>();
            services.TryAddSingleton<IMembershipEventSource>(static sp => sp.GetRequiredService<MembershipEventSource>());
            services.TryAddSingleton<MembershipService>(sp => new MembershipService(
                storeFactory(sp),
                sp.GetRequiredService<INodeIdProvider>(),
                sp.GetRequiredService<CoordinationOptions>(),
                sp.GetRequiredService<MembershipEventSource>(),
                sp.GetService<IHostApplicationLifetime>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MembershipService>>()
            ));
            services.TryAddSingleton<INodeMembership>(static sp => sp.GetRequiredService<MembershipService>());
            services.TryAddSingleton<ProviderCapabilities>(new ProviderCapabilities(FailoverEligible: true));
            services.TryAddSingleton<MembershipHeartbeatBackgroundService>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, MembershipHeartbeatBackgroundService>(static sp =>
                    sp.GetRequiredService<MembershipHeartbeatBackgroundService>()
                )
            );

            return services;
        }
    }
}
