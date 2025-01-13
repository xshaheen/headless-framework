using Framework.ResourceLocks;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(SqliteTestFixture))]
public sealed class SqliteResourceThrottlingLockProviderTests(SqliteTestFixture fixture, ITestOutputHelper output)
    : ResourceThrottlingLockProviderTestsBase(output)
{
    protected override IThrottlingResourceLockStorage GetLockStorage()
    {
        return fixture.ThrottlingLockStorage;
    }

    [Fact]
    public override Task should_throttle_calls_async()
    {
        return base.should_throttle_calls_async();
    }

    [Fact]
    public override Task should_throttle_concurrent_calls_async()
    {
        return base.should_throttle_concurrent_calls_async();
    }
}
