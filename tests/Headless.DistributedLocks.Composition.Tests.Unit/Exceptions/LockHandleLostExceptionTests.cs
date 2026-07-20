// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests.Exceptions;

public sealed class LockHandleLostExceptionTests : TestBase
{
    [Fact]
    public void should_store_resource_lock_id_and_default_message()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.Guid().ToString();

        // when
        var exception = new LockHandleLostException(resource, leaseId);

        // then
        exception.Resource.Should().Be(resource);
        exception.LeaseId.Should().Be(leaseId);
        exception.Message.Should().Contain(resource).And.Contain(leaseId);
    }

    [Theory]
    [InlineData(null, "lock-id")]
    [InlineData("", "lock-id")]
    [InlineData("resource", null)]
    [InlineData("resource", "")]
    public void should_throw_when_resource_or_lock_id_is_null_or_whitespace(string? resource, string? leaseId)
    {
        var act1 = () => _ = new LockHandleLostException(resource!, leaseId!);
        act1.Should().Throw<ArgumentException>();

        var act2 = () => _ = new LockHandleLostException(resource!, leaseId!, "message");
        act2.Should().Throw<ArgumentException>();

        var act3 = () =>
            _ = new LockHandleLostException(resource!, leaseId!, "message", new InvalidOperationException());
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_inherit_from_distributed_lock_exception()
    {
        // when
        var exception = new LockHandleLostException("resource", "lock-id");

        // then
        exception.Should().BeAssignableTo<DistributedLockException>();
    }

    [Fact]
    public void should_preserve_inner_exception()
    {
        // given
        var cause = new InvalidOperationException("inner");

        // when
        var exception = new LockHandleLostException("resource", "lock-id", "message", cause);

        // then
        exception.InnerException.Should().BeSameAs(cause);
    }
}
