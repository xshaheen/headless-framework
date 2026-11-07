// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// One node's services: Headless Jobs on the in-memory store with its job loops off, and the dashboard with the Jobs
/// module, its stream publisher replaced by a recorder. Nodes built over one store share it as a deployment shares
/// one database.
/// </summary>
internal sealed class JobsModuleNode : IAsyncDisposable
{
    public const string Charge = "billing.charge";
    public const string Report = "report.build";

    private JobsModuleNode(ServiceProvider services, RecordingEventPublisher publisher)
    {
        Services = services;
        Published = publisher;
    }

    public ServiceProvider Services { get; }

    public RecordingEventPublisher Published { get; }

    public JobsModule Module => Services.GetRequiredService<JobsModule>();

    public IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Store =>
        Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    public static JobsModuleNode Create(
        FakeTimeProvider time,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? sharedStore = null,
        string nodeId = "node-a",
        Func<IJobsDashboardBackend, IJobsDashboardBackend>? backend = null
    )
    {
        var publisher = new RecordingEventPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessJobs(jobs =>
            jobs.DisableBackgroundServices()
                .AddModule<DashboardTestJobs>()
                .ConfigureScheduler(scheduler => scheduler.NodeId = nodeId)
        );
        services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddJobs());

        if (sharedStore is not null)
        {
            services.AddSingleton(sharedStore);
        }

        services.AddSingleton<IDashboardEventPublisher>(publisher);

        if (backend is not null)
        {
            var factory = services.Single(x => x.ServiceType == typeof(IJobsDashboardBackend)).ImplementationFactory!;
            services.Replace(
                ServiceDescriptor.Singleton(provider => backend((IJobsDashboardBackend)factory(provider)))
            );
        }

        return new JobsModuleNode(services.BuildServiceProvider(), publisher);
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    public static TimeJobEntity FailedJob(
        DateTimeOffset now,
        string function = Charge,
        string fingerprint = "v1:charge-declined"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Status = JobStatus.Failed,
            ExecutionTime = now.UtcDateTime.AddMinutes(-10),
            ExecutedAt = now.AddMinutes(-5),
            ExceptionMessage = "Card declined",
            FailureFingerprint = fingerprint,
            CreatedAt = now.AddHours(-1),
            UpdatedAt = now.AddMinutes(-5),
        };

    public static TimeJobEntity IdleJob(DateTimeOffset now, string function = Charge) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Status = JobStatus.Idle,
            ExecutionTime = now.UtcDateTime.AddMinutes(-1),
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
        };

    public static CronJobEntity CronJob(string function = Report, bool paused = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 0 * * * *",
            IsPaused = paused,
            ScheduleRevision = 1,
        };
}

/// <summary>Registers the two functions the dashboard tests schedule.</summary>
public sealed class DashboardTestJobs : IJobsModule
{
    private DashboardTestJobs() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [JobsModuleNode.Charge] = _Registration(),
                [JobsModuleNode.Report] = _Registration(),
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [JobsModuleNode.Charge] = new(JobsModuleNode.Charge, null, string.Empty, JobPriority.Normal, 0),
                [JobsModuleNode.Report] = new(JobsModuleNode.Report, null, string.Empty, JobPriority.Normal, 0),
            }
        );
    }

    private static JobFunctionRegistration _Registration() =>
        new()
        {
            CronExpression = string.Empty,
            Priority = JobPriority.Normal,
            Delegate = static (_, _, _) => Task.CompletedTask,
            MaxConcurrency = 0,
        };
}

/// <summary>Records every change published to this node's streams, and lets a test wait for one.</summary>
internal sealed class RecordingEventPublisher : IDashboardEventPublisher
{
    private static readonly TimeSpan _Limit = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly List<DashboardChange> _changes = [];

    public IReadOnlyList<DashboardChange> Changes
    {
        get
        {
            lock (_lock)
            {
                return [.. _changes];
            }
        }
    }

    public void Publish(DashboardChange change)
    {
        lock (_lock)
        {
            _changes.Add(change);
        }
    }

    public IReadOnlyList<DashboardChange> For(Guid id) =>
        [.. Changes.Where(change => string.Equals(change.Target.Id, id.ToString(), StringComparison.Ordinal))];

    /// <summary>Waits in real time until a change for <paramref name="id"/> is published.</summary>
    public async Task<DashboardChange> WaitForAsync(Guid id, CancellationToken cancellationToken)
    {
        var limit = DateTime.UtcNow + _Limit;

        while (DateTime.UtcNow < limit)
        {
            if (For(id) is [var first, ..])
            {
                return first;
            }

            await Task.Delay(10, cancellationToken);
        }

        throw new TimeoutException($"No change for {id} was published.");
    }
}
