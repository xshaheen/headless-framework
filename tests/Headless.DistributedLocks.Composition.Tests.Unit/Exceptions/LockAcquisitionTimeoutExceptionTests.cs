// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests.Exceptions;

public sealed class LockAcquisitionTimeoutExceptionTests : TestBase
{
    [Fact]
    public void should_store_resource_and_default_message()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var exception = new LockAcquisitionTimeoutException(resource);

        // then
        exception.Resource.Should().Be(resource);
        exception.Message.Should().Contain(resource);
    }

    [Fact]
    public void should_store_resource_and_custom_message()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var message = Faker.Lorem.Sentence();

        // when
        var exception = new LockAcquisitionTimeoutException(resource, message);

        // then
        exception.Resource.Should().Be(resource);
        exception.Message.Should().Be(message);
    }

    [Fact]
    public void should_store_inner_exception()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var inner = new InvalidOperationException(Faker.Lorem.Sentence());

        // when
        var exception = new LockAcquisitionTimeoutException(resource, "timeout", inner);

        // then
        exception.Resource.Should().Be(resource);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_resource_is_null_or_whitespace(string? resource)
    {
        // when
        var act = () => new LockAcquisitionTimeoutException(resource!);

        // then
        act.Should().Throw<ArgumentException>().WithParameterName(nameof(resource));
    }

    [Fact]
    public void should_inherit_from_distributed_lock_exception()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var exception = new LockAcquisitionTimeoutException(resource);

        // then
        exception.Should().BeAssignableTo<DistributedLockException>();
    }
}
