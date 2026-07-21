// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Testing.Tests;

namespace Tests;

public sealed class AzureServiceBusResourceCleanupTests : TestBase
{
    [Fact]
    public async Task should_remove_tracking_after_successful_delete()
    {
        var tracked = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal) { ["queue"] = 0 };

        await AzureServiceBusResourceCleanup.DeleteTrackedAsync(
            tracked,
            "queue",
            _ => Task.CompletedTask,
            _ => false,
            AbortToken
        );

        tracked.Should().BeEmpty();
    }

    [Fact]
    public async Task should_remove_tracking_when_resource_is_already_deleted()
    {
        var tracked = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal) { ["queue"] = 0 };

        await AzureServiceBusResourceCleanup.DeleteTrackedAsync(
            tracked,
            "queue",
            _ => Task.FromException(new MissingResourceException()),
            exception => exception is MissingResourceException,
            AbortToken
        );

        tracked.Should().BeEmpty();
    }

    [Fact]
    public async Task should_retain_tracking_when_delete_fails()
    {
        var tracked = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal) { ["queue"] = 0 };

        var action = () =>
            AzureServiceBusResourceCleanup
                .DeleteTrackedAsync(
                    tracked,
                    "queue",
                    _ => Task.FromException(new InvalidOperationException("delete failed")),
                    _ => false,
                    AbortToken
                )
                .AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("delete failed");
        tracked.Should().ContainKey("queue");
    }

    [Fact]
    public async Task should_attempt_every_delete_before_aggregating_failures()
    {
        var attempted = new List<string>();
        (string Resource, Func<ValueTask> Delete)[] operations =
        [
            ("queue 'first'", () => _Delete("first", new InvalidOperationException("first failed"))),
            ("queue 'second'", () => _Delete("second")),
            ("topic 'third'", () => _Delete("third", new InvalidOperationException("third failed"))),
        ];

        var action = () => AzureServiceBusResourceCleanup.DeleteAllAsync(operations).AsTask();

        var assertion = await action.Should().ThrowAsync<AggregateException>();
        assertion.Which.InnerExceptions.Should().HaveCount(2);
        attempted.Should().Equal("first", "second", "third");

        ValueTask _Delete(string resource, Exception? exception = null)
        {
            attempted.Add(resource);
            return exception is null ? ValueTask.CompletedTask : ValueTask.FromException(exception);
        }
    }

    private sealed class MissingResourceException : Exception;
}
