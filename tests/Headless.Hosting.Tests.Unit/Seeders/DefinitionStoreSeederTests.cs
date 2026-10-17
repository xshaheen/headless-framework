// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Seeders;

public sealed class DefinitionStoreSeederTests : TestBase
{
    [Fact]
    public async Task should_retry_a_transient_failure_until_the_save_succeeds()
    {
        // given
        var clock = new FakeTimeProvider();
        var sut = new TestSeeder(clock, failures: 2, failure: () => new InvalidOperationException("deadlock"));

        // when
        var seeding = sut.SeedAsync(AbortToken).AsTask();
        while (!seeding.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            await Task.Yield();
        }

        await seeding;

        // then
        sut.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task should_give_up_after_the_last_retry_and_surface_the_error()
    {
        // given
        var clock = new FakeTimeProvider();
        var sut = new TestSeeder(clock, failures: int.MaxValue, failure: () => new InvalidOperationException("down"));

        // when
        var seeding = sut.SeedAsync(AbortToken).AsTask();
        while (!seeding.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            await Task.Yield();
        }

        // then
        await FluentActions
            .Awaiting(() => seeding)
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("down");
        sut.Attempts.Should().Be(DefinitionStoreSeeder.MaxRetries + 1);
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    public async Task should_not_retry_a_failure_that_cannot_pass_next_time(Type exceptionType)
    {
        var sut = new TestSeeder(
            new FakeTimeProvider(),
            failures: int.MaxValue,
            failure: () => (Exception)Activator.CreateInstance(exceptionType, "permanent")!
        );

        await FluentActions.Awaiting(() => sut.SeedAsync(AbortToken).AsTask()).Should().ThrowAsync<Exception>();

        sut.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_do_nothing_when_disabled()
    {
        var sut = new TestSeeder(new FakeTimeProvider(), failures: 0, failure: () => new InvalidOperationException())
        {
            Enabled = false,
        };

        await sut.SeedAsync(AbortToken);

        sut.Attempts.Should().Be(0);
    }

    private sealed class TestSeeder(TimeProvider clock, int failures, Func<Exception> failure)
        : DefinitionStoreSeeder(NullLogger.Instance, clock)
    {
        public int Attempts { get; private set; }

        public bool Enabled { get; init; } = true;

        public override string Name => "TestDefinitions";

        protected override bool IsEnabled => Enabled;

        protected override Task SaveAsync(CancellationToken cancellationToken)
        {
            Attempts++;

            return Attempts <= failures ? Task.FromException(failure()) : Task.CompletedTask;
        }
    }
}
