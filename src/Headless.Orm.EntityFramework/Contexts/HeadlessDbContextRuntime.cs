// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework.ChangeTrackers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework.Contexts;

internal sealed class HeadlessDbContextRuntime
{
    private readonly DbContext _context;
    private readonly IHeadlessEntityModelProcessor _entityProcessor;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();

    public HeadlessDbContextRuntime(DbContext context, IHeadlessEntityModelProcessor entityProcessor)
    {
        _context = context;
        _entityProcessor = entityProcessor;
    }

    public void SyncNavigationTracker()
    {
        _context.ChangeTracker.Tracked += _navigationModifiedTracker.ChangeTrackerTracked;
        _context.ChangeTracker.StateChanged += _navigationModifiedTracker.ChangeTrackerStateChanged;
    }

    public string? TenantId => _entityProcessor.TenantId;

    private ILogger? AuditLogger =>
        field ??= _context.GetServiceOrDefault<ILoggerFactory>()?.CreateLogger(_context.GetType());

    public Task<int> SaveChangesAsync(
        Func<List<EmitterLocalMessages>, IDbContextTransaction, CancellationToken, Task> publishLocalAsync,
        Func<List<EmitterDistributedMessages>, IDbContextTransaction, CancellationToken, Task> publishDistributedAsync,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        return HeadlessSaveChangesRunner.ExecuteAsync(
            _context,
            _entityProcessor,
            _navigationModifiedTracker,
            publishLocalAsync,
            publishDistributedAsync,
            baseSaveChangesAsync,
            AuditLogger,
            acceptAllChangesOnSuccess,
            cancellationToken
        );
    }

    public int SaveChanges(
        Action<List<EmitterLocalMessages>, IDbContextTransaction> publishLocal,
        Action<List<EmitterDistributedMessages>, IDbContextTransaction> publishDistributed,
        Func<bool, int> baseSaveChanges,
        bool acceptAllChangesOnSuccess
    )
    {
        return HeadlessSaveChangesRunner.Execute(
            _context,
            _entityProcessor,
            _navigationModifiedTracker,
            publishLocal,
            publishDistributed,
            baseSaveChanges,
            AuditLogger,
            acceptAllChangesOnSuccess
        );
    }

    public static void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
    }

    public static void ConfigureDefaultSchema(ModelBuilder modelBuilder, string defaultSchema)
    {
        if (!defaultSchema.IsNullOrWhiteSpace())
        {
            modelBuilder.HasDefaultSchema(defaultSchema);
        }
    }

    public void ProcessModelCreating(ModelBuilder modelBuilder)
    {
        _entityProcessor.ProcessModelCreating(modelBuilder);
    }
}
