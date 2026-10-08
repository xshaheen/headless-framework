// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkTransactionLocksTests : TestBase
{
    private readonly IUnitOfWorkTransactionLocks _feature = Substitute.For<IUnitOfWorkTransactionLocks>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();

    public UnitOfWorkTransactionLocksTests()
    {
        _unit.GetFeature<IUnitOfWorkTransactionLocks>().Returns(_feature);
        _unit
            .GetOrAdd(
                Arg.Any<IUnitOfWorkTransactionLocks>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkTransactionLocks, UnitOfWorkTransactionLocks>>()
            )
            .Returns(call =>
                call.Arg<Func<IUnitOfWork, IUnitOfWorkTransactionLocks, UnitOfWorkTransactionLocks>>()(_unit, _feature)
            );
    }

    [Fact]
    public void should_name_the_capable_providers_when_no_feature_is_registered()
    {
        // given
        var unit = Substitute.For<IUnitOfWork>();
        unit.GetFeature<IUnitOfWorkTransactionLocks>().Returns((IUnitOfWorkTransactionLocks?)null);

        // when
        var act = () => unit.TransactionLocks;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*UsePostgreSql*UseSqlServer*");
    }

    [Fact]
    public async Task should_forward_the_bound_unit_and_a_single_resource_set()
    {
        // given
        _GrantAsync(true);
        var timeout = TimeSpan.FromSeconds(3);

        // when
        var handle = await _unit.TransactionLocks.TryAcquireAsync("orders:1", timeout, AbortToken);

        // then — the binding forwards the handle it was created for and the caller's wait unchanged
        handle.Should().Be(new TransactionLockHandle("orders:1"));
        await _feature
            .Received(1)
            .TryAcquireAsync(
                _unit,
                Arg.Is<IReadOnlyList<string>>(r => r.SequenceEqual(new[] { "orders:1" })),
                timeout,
                AbortToken
            );
    }

    [Fact]
    public async Task should_canonicalize_the_set_into_distinct_ordinal_order()
    {
        // given
        _GrantAsync(true);

        // when
        var handles = await _unit.TransactionLocks.AcquireAllAsync(
            ["wallet:b", "Wallet:a", "wallet:a", "wallet:b"],
            cancellationToken: AbortToken
        );

        // then — ordinal: upper case sorts first, and case is identity, not a duplicate
        string[] expected = ["Wallet:a", "wallet:a", "wallet:b"];
        handles.Select(h => h.Resource).Should().Equal(expected);
        await _feature
            .Received(1)
            .TryAcquireAsync(
                _unit,
                Arg.Is<IReadOnlyList<string>>(r => r.SequenceEqual(expected)),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_default_acquire_to_thirty_seconds_and_try_to_one_attempt()
    {
        // given
        _GrantAsync(true);
        _feature.TryAcquire(_unit, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>()).Returns(true);
        var locks = _unit.TransactionLocks;

        // when
        await locks.AcquireAsync("a", cancellationToken: AbortToken);
        await locks.AcquireAllAsync(["a", "b"], cancellationToken: AbortToken);
        locks.Acquire("a");
        locks.AcquireAll(["a", "b"]);
        await locks.TryAcquireAsync("a", cancellationToken: AbortToken);
        await locks.TryAcquireAllAsync(["a", "b"], cancellationToken: AbortToken);
        locks.TryAcquire("a");
        locks.TryAcquireAll(["a", "b"]);

        // then
        var thirtySeconds = TimeSpan.FromSeconds(30);
        await _feature.Received(2).TryAcquireAsync(_unit, Arg.Any<IReadOnlyList<string>>(), thirtySeconds, AbortToken);
        await _feature.Received(2).TryAcquireAsync(_unit, Arg.Any<IReadOnlyList<string>>(), TimeSpan.Zero, AbortToken);
        _feature.Received(2).TryAcquire(_unit, Arg.Any<IReadOnlyList<string>>(), thirtySeconds);
        _feature.Received(2).TryAcquire(_unit, Arg.Any<IReadOnlyList<string>>(), TimeSpan.Zero);
    }

    [Fact]
    public async Task should_return_null_from_try_forms_when_the_provider_reports_contention()
    {
        // given
        _GrantAsync(false);
        _feature.TryAcquire(_unit, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>()).Returns(false);
        var locks = _unit.TransactionLocks;

        // when / then
        (await locks.TryAcquireAsync("a", TimeSpan.FromMilliseconds(500), AbortToken))
            .Should()
            .BeNull();
        (await locks.TryAcquireAllAsync(["a", "b"], TimeSpan.FromMilliseconds(500), AbortToken)).Should().BeNull();
        locks.TryAcquire("a", TimeSpan.FromMilliseconds(500)).Should().BeNull();
        locks.TryAcquireAll(["a", "b"], TimeSpan.FromMilliseconds(500)).Should().BeNull();
    }

    [Fact]
    public async Task should_throw_a_timeout_naming_the_single_resource_or_the_joined_set()
    {
        // given
        _GrantAsync(false);
        _feature.TryAcquire(_unit, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>()).Returns(false);
        var locks = _unit.TransactionLocks;

        // when
        var single = async () => await locks.AcquireAsync("a", TimeSpan.FromSeconds(5), AbortToken);
        var set = async () => await locks.AcquireAllAsync(["b", "a"], TimeSpan.FromSeconds(5), AbortToken);
        var syncSet = () => locks.AcquireAll(["b", "a"], TimeSpan.Zero);

        // then
        (await single.Should().ThrowAsync<LockAcquisitionTimeoutException>())
            .Which.Resource.Should()
            .Be("a");
        (await set.Should().ThrowAsync<LockAcquisitionTimeoutException>()).Which.Resource.Should().Be("a+b");
        syncSet
            .Should()
            .Throw<LockAcquisitionTimeoutException>()
            .Where(e => e.Resource == "a+b" && e.Message.Contains("first attempt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1000)]
    public async Task should_reject_a_negative_wait_before_calling_the_provider(int milliseconds)
    {
        // given
        var locks = _unit.TransactionLocks;
        var timeout = TimeSpan.FromMilliseconds(milliseconds);

        // when
        var act = async () => await locks.AcquireAsync("a", timeout, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _feature.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_pass_an_infinite_wait_through_unchanged()
    {
        // given
        _GrantAsync(true);

        // when
        await _unit.TransactionLocks.AcquireAsync("a", Timeout.InfiniteTimeSpan, AbortToken);

        // then
        await _feature
            .Received(1)
            .TryAcquireAsync(_unit, Arg.Any<IReadOnlyList<string>>(), Timeout.InfiniteTimeSpan, AbortToken);
    }

    [Fact]
    public void should_reject_an_empty_set_or_a_blank_name_before_calling_the_provider()
    {
        // given
        var locks = _unit.TransactionLocks;

        // when
        var empty = () => locks.TryAcquireAll([]);
        var blank = () => locks.TryAcquireAll(["a", " "]);
        var nullName = () => locks.TryAcquireAll(["a", null!]);
        var nullSet = () => locks.TryAcquireAll(null!);

        // then
        empty.Should().Throw<ArgumentException>();
        blank.Should().Throw<ArgumentException>();
        nullName.Should().Throw<ArgumentNullException>();
        nullSet.Should().Throw<ArgumentNullException>();
        _feature.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void should_compare_handles_by_resource()
    {
        new TransactionLockHandle("orders:1").Should().Be(new TransactionLockHandle("orders:1"));
        new TransactionLockHandle("orders:1").Should().NotBe(new TransactionLockHandle("orders:2"));
    }

    private void _GrantAsync(bool acquired)
    {
        _feature
            .TryAcquireAsync(_unit, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(acquired));
    }
}
