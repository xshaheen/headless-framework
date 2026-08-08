// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Transactions.Config;
using Headless.Couchbase.Clusters;
using Headless.Testing.Tests;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class CouchbaseConfigurationProviderTests : TestBase
{
    [Fact]
    public async Task should_return_shared_cluster_options_for_every_key()
    {
        var options = new ClusterOptions();
        var sut = new CouchbaseClusterOptionsProvider(options);

        var first = await sut.GetAsync("primary", AbortToken);
        var second = await sut.GetAsync("analytics", AbortToken);

        first.Should().BeSameAs(options);
        second.Should().BeSameAs(options);
    }

    [Fact]
    public async Task should_apply_transaction_configuration_once_and_reuse_builder()
    {
        var configured = 0;
        var sut = new CouchbaseTransactionConfigProvider(builder =>
        {
            configured++;
            builder.ExpirationTime(TimeSpan.FromSeconds(30));
        });

        var first = await sut.GetAsync("primary", AbortToken);
        var second = await sut.GetAsync("analytics", AbortToken);

        configured.Should().Be(1);
        first.Should().BeSameAs(second);
        first.Build().ExpirationTime.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("Development", 500)]
    [InlineData("Production", 100)]
    public async Task should_build_environment_aware_transaction_defaults(string environmentName, int expirySeconds)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var sut = new DefaultCouchbaseTransactionConfigProvider(environment);

        var config = (await sut.GetAsync("primary", AbortToken)).Build();

        config.KeyValueTimeout.Should().Be(TimeSpan.FromSeconds(10));
        config.ExpirationTime.Should().Be(TimeSpan.FromSeconds(expirySeconds));
        config.DurabilityLevel.Should().Be(DurabilityLevel.Majority);
        config.CleanupLostAttempts.Should().BeTrue();
        config.CleanupClientAttempts.Should().BeTrue();
        config.CleanupWindow.Should().Be(TimeSpan.FromSeconds(120));
    }
}
