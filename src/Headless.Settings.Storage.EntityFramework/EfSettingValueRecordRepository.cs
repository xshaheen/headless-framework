// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings;

/// <summary>
/// EF Core implementation of <see cref="ISettingValueRecordRepository"/> that stores
/// <see cref="SettingValueRecord"/> entities via a pooled <typeparamref name="TContext"/> and
/// publishes <see cref="Headless.Domain.EntityChangedEventData{T}"/> events after mutations.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type registered with the DI container.</typeparam>
/// <param name="dbFactory">Factory used to create <typeparamref name="TContext"/> instances per operation.</param>
/// <param name="localPublisher">Local event bus used to publish change events after inserts, updates, and deletes.</param>
public sealed class EfSettingValueRecordRepository<TContext>(
    IDbContextFactory<TContext> dbFactory,
    ILocalEventBus localPublisher
) : ISettingValueRecordRepository
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<SettingValueRecord>()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(
                s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<SettingValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = db.Set<SettingValueRecord>().AsNoTracking().Where(s => s.Name == name);

        if (providerName != null)
        {
            query = query.Where(s => s.ProviderName == providerName);
        }

        if (providerKey != null)
        {
            query = query.Where(s => s.ProviderKey == providerKey);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<SettingValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<SettingValueRecord>()
            .AsNoTracking()
            .Where(s => names.Contains(s.Name) && s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Set<SettingValueRecord>()
            .AsNoTracking()
            .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingValueRecord>().Add(setting);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingValueRecord>().Update(setting);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await localPublisher
            .PublishAsync(new EntityChangedEventData<SettingValueRecord>(setting), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        IReadOnlyCollection<SettingValueRecord> settings,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        db.Set<SettingValueRecord>().RemoveRange(settings);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var setting in settings)
        {
            await localPublisher
                .PublishAsync(new EntityChangedEventData<SettingValueRecord>(setting), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
