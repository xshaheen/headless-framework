// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Testing.Tests;
using Headless.Threading;

namespace Tests;

public sealed class SqlAutonomousReadTests : TestBase
{
    [Fact]
    public async Task should_read_on_an_opened_connection_without_beginning_a_transaction()
    {
        var connections = new List<FakeConnection>();
        var opened = false;

        var result = await SqlAutonomousRead.RunAsync(
            "test.read",
            () => SqlAutonomousFakes.Track(connections),
            (connection, _) =>
            {
                opened = connection.State == System.Data.ConnectionState.Open;

                return Task.FromResult(42);
            },
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(42);
        opened.Should().BeTrue();
        connections.Should().ContainSingle();
        connections[0].Transactions.Should().BeEmpty("one statement needs no BEGIN or COMMIT round trip");
        connections[0].WasDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_refuse_a_blank_operation_before_opening_a_connection(string operation)
    {
        var connections = new List<FakeConnection>();

        var act = async () =>
            await SqlAutonomousRead.RunAsync(
                operation,
                () => SqlAutonomousFakes.Track(connections),
                static (_, _) => Task.FromResult(42),
                TimeProvider.System,
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(operation));
        connections.Should().BeEmpty();
    }

    [Theory]
    [InlineData("driver-flag")]
    [InlineData("40001")]
    [InlineData("40P01")]
    public async Task should_retry_a_transient_fault_on_a_new_connection(string fault)
    {
        var connections = new List<FakeConnection>();
        var attempts = 0;

        var result = await SqlAutonomousRead.RunAsync(
            "test.read",
            () => SqlAutonomousFakes.Track(connections),
            (_, _) => ++attempts == 1 ? throw SqlAutonomousFakes.Transient(fault) : Task.FromResult(attempts),
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(2);
        connections.Should().HaveCount(2);
        connections[0].WasDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_retry_a_non_transient_fault()
    {
        var connections = new List<FakeConnection>();
        var fault = new FakeDbException(isTransient: false, sqlState: "42P01");

        var act = async () =>
            await SqlAutonomousRead.RunAsync<int>(
                "test.read",
                () => SqlAutonomousFakes.Track(connections),
                (_, _) => throw fault,
                TimeProvider.System,
                AbortToken
            );

        (await act.Should().ThrowAsync<FakeDbException>()).Which.Should().BeSameAs(fault);
        connections.Should().ContainSingle();
    }

    [Fact]
    public async Task should_give_up_after_the_last_attempt_and_surface_the_fault()
    {
        var connections = new List<FakeConnection>();

        var act = async () =>
            await SqlAutonomousRead.RunAsync<int>(
                "test.read",
                () => SqlAutonomousFakes.Track(connections),
                (_, _) => throw SqlAutonomousFakes.Transient("40P01"),
                TimeProvider.System,
                AbortToken
            );

        await act.Should().ThrowAsync<FakeDbException>();
        connections.Should().HaveCount(TransientRetry.MaxAttempts);
    }
}
