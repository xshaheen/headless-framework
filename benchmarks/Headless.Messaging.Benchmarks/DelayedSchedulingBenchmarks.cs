// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Benchmarks;

/// <summary>
/// Allocation and CPU proxy for the delayed scheduler's bounded claim path. Provider integration
/// benchmarks remain responsible for database statement count, transaction duration, and lock waits.
/// </summary>
[MemoryDiagnoser]
public class DelayedSchedulingBenchmarks
{
    private InMemoryDataStorage _storage = null!;

    [Params(100, 1000)]
    public int BatchSize { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        var options = Options.Create(new MessagingOptions { Version = "benchmark", SchedulerBatchSize = BatchSize });
        var membership = new NullNodeMembership();
        membership.RegisterAsync().AsTask().GetAwaiter().GetResult();
        _storage = new InMemoryDataStorage(
            options,
            new JsonUtf8Serializer(options),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            membership
        );

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
        for (var index = 0; index < BatchSize; index++)
        {
            var origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = $"delayed-{index}" },
                index
            );
            var stored = _storage
                .StoreMessageAsync(
                    "delayed-benchmark",
                    new MediumMessage
                    {
                        StorageId = Guid.Empty,
                        Origin = origin,
                        Content = string.Empty,
                        IntentType = IntentType.Bus,
                        ExpiresAt = expiresAt,
                    }
                )
                .AsTask()
                .GetAwaiter()
                .GetResult();
            stored.ExpiresAt = expiresAt;
            _storage.ChangePublishStateAsync(stored, StatusName.Delayed).AsTask().GetAwaiter().GetResult();
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> LegacySerialTransitions()
    {
        var changed = 0;
        await _storage.ScheduleMessagesOfDelayedAsync(
            async (_, messages) =>
            {
                foreach (var message in messages)
                {
                    if (await _storage.ChangePublishStateAsync(message, StatusName.Queued).ConfigureAwait(false))
                    {
                        changed++;
                    }
                }
            }
        );
        return changed;
    }

    [Benchmark]
    public async Task<int> AtomicClaim()
    {
        var claimed = await _storage.ClaimDelayedMessagesAsync().ConfigureAwait(false);
        return claimed.Count;
    }
}
