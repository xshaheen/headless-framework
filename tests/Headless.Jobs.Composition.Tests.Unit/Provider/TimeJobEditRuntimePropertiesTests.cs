// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The relational store protects the columns in <see cref="TimeJobEditGuard.RuntimeProperties"/> from an edit by
/// name, while the in-memory store copies them from the stored row by hand. The two lists must name the same columns,
/// or the stores disagree about what an edit may write.
/// </summary>
public sealed class TimeJobEditRuntimePropertiesTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);

    // Scheduling captures these once; an edit keeps them as well, though they are not run state.
    private static readonly string[] _Lineage =
    [
        nameof(BaseJobEntity.TenantId),
        nameof(BaseJobEntity.CorrelationId),
        nameof(BaseJobEntity.CausationId),
        nameof(TimeJobEntity.TraceParent),
    ];

    // Identity, graph shape, validated or reference-typed members, and the keyed-job metadata an ordinary edit
    // rejects outright, none of which an edit test can vary freely.
    private static readonly string[] _NotVaried =
    [
        nameof(TimeJobEntity.Id),
        nameof(TimeJobEntity.Function),
        nameof(TimeJobEntity.ContractVersion),
        nameof(TimeJobEntity.ParentId),
        nameof(TimeJobEntity.Parent),
        nameof(TimeJobEntity.Children),
        nameof(TimeJobEntity.Request),
        nameof(TimeJobEntity.RetryIntervals),
        nameof(TimeJobEntity.BusinessKey),
        nameof(TimeJobEntity.IntentFingerprint),
        nameof(TimeJobEntity.FingerprintAlgorithm),
        nameof(TimeJobEntity.Generation),
        nameof(TimeJobEntity.IsCurrentGeneration),
        // The in-memory store's copies do not carry it, so a read never shows the stored value.
        nameof(BaseJobEntity.IsSystemJob),
    ];

    [Fact]
    public async Task should_keep_exactly_the_guard_runtime_properties_and_lineage_when_an_edit_lands()
    {
        var provider = _CreateProvider();
        var job = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(10),
            UpdatedAt = _Now,
        };
        await provider.AddTimeJobsAsync([job], AbortToken);

        var copy = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        // The store may keep the caller's instance, so remember what the edit asked for before it runs.
        var requested = new Dictionary<PropertyInfo, object>();
        foreach (var property in _VariableProperties())
        {
            requested[property] = _DifferentValue(property.PropertyType, property.GetValue(copy));
            property.SetValue(copy, requested[property]);
        }

        (await provider.UpdateTimeJobsAsync([copy], AbortToken)).Should().Be(1);

        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        var keptFromStore = requested
            .Where(pair => !Equals(pair.Key.GetValue(stored), pair.Value))
            .Select(pair => pair.Key.Name);

        keptFromStore.Should().BeEquivalentTo(TimeJobEditGuard.RuntimeProperties.Concat(_Lineage));
    }

    private static IEnumerable<PropertyInfo> _VariableProperties()
    {
        return typeof(FakeTimeJob)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
                property.GetSetMethod(nonPublic: true) is not null
                && !_NotVaried.Contains(property.Name, StringComparer.Ordinal)
            );
    }

    private static object _DifferentValue(Type type, object? current)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        object candidate = target switch
        {
            _ when target == typeof(string) => "changed",
            _ when target == typeof(int) => 7,
            _ when target == typeof(long) => 7L,
            _ when target == typeof(double) => 42.5,
            _ when target == typeof(bool) => current is not true,
            _ when target == typeof(DateTime) => new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ when target == typeof(DateTimeOffset) => new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ when target.IsEnum => Enum.GetValues(target).Cast<object>().First(value => !value.Equals(current)),
            _ => throw new InvalidOperationException($"Add a changed value for {type} to this test."),
        };

        if (Equals(candidate, current))
        {
            throw new InvalidOperationException($"The changed value for {type} equals the stored one.");
        }

        return candidate;
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a" });

        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }
}
