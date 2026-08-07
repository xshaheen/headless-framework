// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Couchbase;
using Headless.Couchbase.Clusters;
using Headless.Couchbase.ContextProvider;
using Headless.Couchbase.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupCouchbaseTests
{
    [Fact]
    public void should_register_framework_services_as_singletons()
    {
        var services = new ServiceCollection();
        Type[] serviceTypes =
        [
            typeof(ICouchbaseClustersProvider),
            typeof(IBucketContextProvider),
            typeof(ICouchbaseManager),
            typeof(ICouchbaseAssemblyCollectionsReader),
        ];

        services.AddHeadlessCouchbase();

        foreach (var serviceType in serviceTypes)
        {
            services
                .Should()
                .ContainSingle(x => x.ServiceType == serviceType)
                .Which.Lifetime.Should()
                .Be(ServiceLifetime.Singleton);
        }
    }

    [Fact]
    public void should_preserve_consumer_overrides()
    {
        var services = new ServiceCollection();
        var manager = Substitute.For<ICouchbaseManager>();
        services.AddSingleton(manager);

        services.AddHeadlessCouchbase();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICouchbaseManager>().Should().BeSameAs(manager);
    }

    [Fact]
    public void should_bind_manager_options_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [nameof(CouchbaseManagerOptions.MaxRetries)] = "5",
                    [nameof(CouchbaseManagerOptions.RetryDelay)] = "00:00:00.250",
                    [nameof(CouchbaseManagerOptions.Timeout)] = "00:00:20",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddHeadlessCouchbase(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CouchbaseManagerOptions>>().Value;

        options.MaxRetries.Should().Be(5);
        options.RetryDelay.Should().Be(TimeSpan.FromMilliseconds(250));
        options.Timeout.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void should_configure_manager_options_with_service_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RetrySettings(7));
        services.AddHeadlessCouchbase(
            (options, provider) => options.MaxRetries = provider.GetRequiredService<RetrySettings>().MaxRetries
        );

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<CouchbaseManagerOptions>>().Value.MaxRetries.Should().Be(7);
    }

    [Theory]
    [InlineData(0, 0, 1000)]
    [InlineData(1, -1, 1000)]
    [InlineData(1, 0, 10)]
    [InlineData(1, 0, 86_400_000)]
    public void should_reject_invalid_manager_options(int maxRetries, int retryDelayMs, int timeoutMs)
    {
        var services = new ServiceCollection();
        services.AddHeadlessCouchbase(options =>
        {
            options.MaxRetries = maxRetries;
            options.RetryDelay = TimeSpan.FromMilliseconds(retryDelayMs);
            options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
        });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<CouchbaseManagerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    private sealed record RetrySettings(int MaxRetries);
}
