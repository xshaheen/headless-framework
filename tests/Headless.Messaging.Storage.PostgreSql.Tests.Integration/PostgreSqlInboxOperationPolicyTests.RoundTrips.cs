// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed partial class PostgreSqlInboxOperationPolicyTests
{
    private static readonly ActivitySource _CountingSource = new("Tests.RoundTrips");

    [Fact]
    public async Task should_admit_a_first_delivery_in_one_command()
    {
        await using var provider = _CreateProvider();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();

        var (commands, message) = await _CountCommandsAsync(() => _AdmitAsync(storage, MessageLane.Bus).AsTask());

        message.InboxGeneration.Should().NotBeNull();
        commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(250)]
    public async Task should_sweep_expired_receipts_with_one_lock_per_receipt_and_one_delete_per_chunk(int receipts)
    {
        await using var provider = _CreateProvider();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        for (var i = 0; i < receipts; i++)
        {
            await storage.GetInboxOperationsApi().HoldAsync(_Request(Guid.NewGuid(), StatusName.Succeeded), AbortToken);
        }

        await AgeHistoryAsync(provider, TimeSpan.FromDays(100));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, receipts, AbortToken)).Should().Be(receipts);

        var (commands, deleted) = await _CountCommandsAsync(() =>
            storage.DeleteExpiredInboxReceiptsAsync(cutoffs, receipts, AbortToken).AsTask()
        );

        deleted.Should().Be(receipts);
        // One discovery query per operation type, one operation lock per receipt, and one set-based delete for each
        // transaction of up to 100 receipts.
        commands
            .Should()
            .HaveCount(Enum.GetValues<MessagingOperationType>().Length + receipts + ((receipts + 99) / 100));
    }

    /// <summary>
    /// Counts the commands Npgsql runs under one root activity, so commands other tests run in parallel are not
    /// counted: each command's activity joins the ambient trace the storage call flows in.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Commands, T Result)> _CountCommandsAsync<T>(Func<Task<T>> action)
    {
        var commands = new System.Collections.Concurrent.ConcurrentQueue<string>();
        ActivityTraceId? trace = null;
        using var listener = new ActivityListener
        {
            // By name: comparing to the source itself would create it inside this callback, too late to be listened to.
            ShouldListenTo = static source => source.Name is "Npgsql" or "Tests.RoundTrips",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (
                    string.Equals(activity.Source.Name, "Npgsql", StringComparison.Ordinal)
                    && activity.TraceId == trace
                )
                {
                    commands.Enqueue(activity.DisplayName);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        T result;
        using (var root = _CountingSource.StartActivity("count"))
        {
            root.Should().NotBeNull();
            trace = root!.TraceId;
            result = await action();
        }

        return ([.. commands], result);
    }
}
