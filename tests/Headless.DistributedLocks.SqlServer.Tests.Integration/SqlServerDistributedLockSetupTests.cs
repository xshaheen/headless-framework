// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SqlServerDistributedLockSetupTests : TestBase
{
    [Fact]
    public async Task should_register_mutex_and_reader_writer_providers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerDistributedLocks(options =>
        {
            options.ConnectionString = "Server=localhost;Database=headless;User Id=sa;Password=Password1!";
            options.EnableFencing = false;
        });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedLock>().Should().BeOfType<ConnectionScopedDistributedLock>();
        provider.GetRequiredService<IDistributedReadWriteLock>().Should().BeOfType<ConnectionScopedReadWriteLock>();
        provider.GetRequiredService<IConnectionScopedLockStorage>().BlocksServerSide.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "dbo", "prefix:", 30)] // empty connection string
    [InlineData("Server=localhost;", "invalid schema name!", "prefix:", 30)] // invalid schema identifier
    [InlineData("Server=localhost;", "dbo", "", 30)] // empty prefix
    [InlineData("Server=localhost;", "dbo", "prefix:", 0)] // zero timeout
    [InlineData("Server=localhost;", "dbo", "prefix:", -5)] // negative timeout
    public void should_fail_validation_when_options_are_invalid(
        string connectionString,
        string schema,
        string keyPrefix,
        int commandTimeoutSeconds
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerDistributedLocks(options =>
        {
            options.ConnectionString = connectionString;
            options.Schema = schema;
            options.KeyPrefix = keyPrefix;
            options.CommandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
        });

        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<SqlServerDistributedLockOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }
}
