// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class HeadlessResourceReaperFixtureTests : TestBase
{
    [Fact]
    public async Task should_start_once_without_reporting_when_the_first_attempt_succeeds()
    {
        // given
        var attempts = 0;
        var reports = new List<string>();

        // when
        await HeadlessResourceReaperFixture.StartAsync(
            _ =>
            {
                attempts++;
                return Task.CompletedTask;
            },
            reports.Add,
            AbortToken
        );

        // then
        attempts.Should().Be(1);
        reports.Should().BeEmpty();
    }

    [Fact]
    public async Task should_retry_once_and_report_when_the_first_attempt_times_out()
    {
        // given
        var attempts = 0;
        var reports = new List<string>();

        // when
        await HeadlessResourceReaperFixture.StartAsync(
            _ =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new ResourceReaperException("Initialization has been cancelled."))
                    : Task.CompletedTask;
            },
            reports.Add,
            AbortToken
        );

        // then
        attempts.Should().Be(2);
        reports
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("attempt 1 of 2")
            .And.Contain("Initialization has been cancelled.");
    }

    [Fact]
    public async Task should_name_an_infrastructure_failure_when_both_attempts_fail()
    {
        // given
        var attempts = 0;

        // when
        var act = () =>
            HeadlessResourceReaperFixture.StartAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromException(new ResourceReaperException("Initialization has been cancelled."));
                },
                _ => { },
                AbortToken
            );

        // then
        var error = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        error.Which.Message.Should().StartWith("Docker infrastructure failure, not a test failure");
        error.Which.InnerException.Should().BeOfType<ResourceReaperException>();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_not_retry_when_the_run_is_cancelled()
    {
        // given
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var attempts = 0;

        // when
        var act = () =>
            HeadlessResourceReaperFixture.StartAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromException(new ResourceReaperException("Initialization has been cancelled."));
                },
                _ => { },
                cancellation.Token
            );

        // then
        await act.Should().ThrowExactlyAsync<ResourceReaperException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_not_retry_an_error_that_is_not_a_reaper_failure()
    {
        // given
        var attempts = 0;

        // when
        var act = () =>
            HeadlessResourceReaperFixture.StartAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromException(new InvalidOperationException("Docker is not running."));
                },
                _ => { },
                AbortToken
            );

        // then
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("Docker is not running.");
        attempts.Should().Be(1);
    }
}
