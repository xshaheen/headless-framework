// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class PostgresDistributedLockSetupTests : TestBase
{
    [Fact]
    public async Task should_register_mutex_and_reader_writer_providers()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = "Host=localhost;Database=headless";
                options.EnablePushWakeup = false;
            })
        );

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedLock>().Should().BeOfType<ConnectionScopedDistributedLock>();
        provider.GetRequiredService<IDistributedReadWriteLock>().Should().BeOfType<ConnectionScopedReadWriteLock>();
    }

    [Theory]
    [InlineData("", "prefix:", 100, 30, 30)] // empty connection string (and null DataSource)
    [InlineData("Host=localhost;", "", 100, 30, 30)] // empty prefix
    [InlineData("Host=localhost;", "prefix:", 0, 30, 30)] // zero polling fallback
    [InlineData("Host=localhost;", "prefix:", 100, 0, 30)] // zero command timeout
    [InlineData("Host=localhost;", "prefix:", 100, 30, -5)] // negative keep alive
    public void should_fail_validation_when_options_are_invalid(
        string connectionString,
        string keyPrefix,
        int pollingFallbackMs,
        int commandTimeoutSeconds,
        int keepAliveSeconds
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
                options.KeyPrefix = keyPrefix;
                options.PollingFallback = TimeSpan.FromMilliseconds(pollingFallbackMs);
                options.CommandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
                options.KeepAlive = TimeSpan.FromSeconds(keepAliveSeconds);
            })
        );

        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<PostgresDistributedLockOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }
}
