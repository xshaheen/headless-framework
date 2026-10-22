// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Tests;

[Collection<RedisTestFixture>]
public sealed class RedisDistributedLockSetupTests(RedisTestFixture fixture) : TestBase
{
    [Fact]
    public void should_fail_validation_when_add_headless_distributed_locks_options_are_invalid()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(fixture.ConnectionMultiplexer);
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(options =>
            {
                options.MaxResourceNameLength = 0; // invalid per DistributedLockOptionsValidator
            });
            setup.UseRedis();
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DistributedLockOptions>>().Value;

        // then
        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
    }
}
