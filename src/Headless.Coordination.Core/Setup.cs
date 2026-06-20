// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Core;
using Headless.Serializer;
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
        public IServiceCollection AddHeadlessCoordination(Action<HeadlessCoordinationSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessCoordinationSetupBuilder(services);
            configure(setup);

            return _AddCoordinationProviderCore(services, setup);
        }

        internal IServiceCollection AddCoordinationCore<TStore>()
            where TStore : class, IMembershipStore
        {
            services.AddOptions<CoordinationOptions, CoordinationOptionsValidator>();

            return services._AddCoordinationCore<TStore>();
        }

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

            // Keyed so consumers can override coordination metadata/endpoint serialization independently of the
            // global IJsonSerializer by pre-registering their own keyed serializer under the same key. Defaults to
            // web JSON (SystemJsonSerializer + DefaultJsonOptionsProvider).
            services.TryAddKeyedSingleton<IJsonSerializer>(
                CoordinationOptions.JsonSerializerServiceKey,
                static (_, _) => new SystemJsonSerializer(new DefaultJsonOptionsProvider())
            );

            services.TryAddSingleton(TimeProvider.System);
            services.AddHeadlessGuidGenerator();
            services.TryAddSingleton<INodeIdProvider, DefaultNodeIdProvider>();
            services.TryAddSingleton<MembershipEventSource>();
            services.TryAddSingleton<IMembershipEventSource>(static sp =>
                sp.GetRequiredService<MembershipEventSource>()
            );

            services.TryAddSingleton<MembershipService>(sp => new MembershipService(
                storeFactory(sp),
                sp.GetRequiredService<INodeIdProvider>(),
                sp.GetRequiredService<CoordinationOptions>(),
                sp.GetRequiredService<MembershipEventSource>(),
                sp.GetService<IHostApplicationLifetime>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MembershipService>>()
            ));

            // AddSingleton (not TryAdd) so this real membership wins by last-registration even when a consumer
            // package registered a NullNodeMembership fallback first (e.g. Headless.Messaging). Coordination is the
            // stronger, explicit provider and should always replace the null default. A custom INodeMembership must
            // therefore be registered AFTER AddHeadlessCoordination to take effect.
            services.AddSingleton<INodeMembership>(static sp => sp.GetRequiredService<MembershipService>());
            services.TryAddSingleton<MembershipHeartbeatBackgroundService>();

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, MembershipHeartbeatBackgroundService>(static sp =>
                    sp.GetRequiredService<MembershipHeartbeatBackgroundService>()
                )
            );

            return services;
        }
    }

    private static IServiceCollection _AddCoordinationProviderCore(
        IServiceCollection services,
        HeadlessCoordinationSetupBuilder setup
    )
    {
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.Extensions.Count == 0
                    ? "Headless.Coordination requires exactly one provider. Call one of `UsePostgreSql`, `UseRedis`, or `UseSqlServer`."
                    : "Headless.Coordination requires exactly one provider. Multiple providers were configured."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(CoordinationProviderRegistration)))
        {
            throw new InvalidOperationException(
                "Headless.Coordination requires exactly one provider. Multiple providers were configured."
            );
        }

        var extension = setup.Extensions.Single();
        var extensionTypeName = extension.GetType().FullName ?? "unknown";
        services.AddSingleton(new CoordinationProviderRegistration(extensionTypeName));

        extension.AddServices(services);

        return services;
    }

    private sealed record CoordinationProviderRegistration(string Provider);
}
