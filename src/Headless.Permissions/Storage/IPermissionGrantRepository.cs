// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Permissions;

/// <summary>
/// Storage contract for persisting and querying permission grant records. Implemented by the EF Core /
/// PostgreSQL / SQL Server provider packages.
/// </summary>
/// <remarks>
/// New members arrive as default interface members, so a third-party implementation keeps compiling. The enlisted
/// members (<c>*EnlistedAsync</c>) run inside a caller's <see cref="IUnitOfWork" /> and default to throwing
/// <see cref="NotSupportedException" />; an implementation that cannot join a unit leaves them, and
/// <c>unit.Permissions</c> then refuses before any write.
/// </remarks>
public interface IPermissionGrantRepository
{
    /// <summary>
    /// Finds a single grant record by the (name, providerName, providerKey) composite key.
    /// Returns <see langword="null"/> when no matching record exists.
    /// </summary>
    /// <param name="name">Permission name.</param>
    /// <param name="providerName">The grant provider name (e.g. <c>"User"</c> or <c>"Role"</c>).</param>
    /// <param name="providerKey">The provider-specific subject key (e.g. user id or role name).</param>
    Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns all grant records for a given provider/subject, regardless of permission name. Useful for loading
    /// the full grant set for a single principal.
    /// </summary>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns grant records for the specified permission <paramref name="names"/> scoped to a single
    /// provider/subject. Prefer this overload for batch permission checks to avoid N+1 queries.
    /// </summary>
    /// <param name="names">The permission names to look up. Must not be <see langword="null"/>.</param>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns grant records for the specified permission <paramref name="names"/> scoped to several
    /// subjects of one provider, in a single query. This is the one-call read for checks that span every
    /// target of a provider, such as every role a user holds.
    /// </summary>
    /// <param name="names">The permission names to look up. Must not be <see langword="null"/>.</param>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKeys">The provider-specific subject keys. Must not be <see langword="null"/> or empty.</param>
    Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inserts a single new grant record.</summary>
    Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default);

    /// <summary>Inserts multiple new grant records in a single batch.</summary>
    Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes a single grant record.</summary>
    Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken);

    /// <summary>Deletes multiple grant records in a single batch.</summary>
    Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns all grant records for a given provider/subject, read inside <paramref name="unitOfWork" />'s
    /// transaction so the read sees the unit's own uncommitted writes.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task<List<PermissionGrantRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>
    /// Returns grant records for the specified permission <paramref name="names"/> scoped to a single
    /// provider/subject, read inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="names">The permission names to look up.</param>
    /// <param name="providerName">The grant provider type name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the read.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task<List<PermissionGrantRecord>> GetListEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>Inserts grant records inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the insert.</param>
    /// <param name="permissionGrants">The records to insert.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task InsertManyEnlistedAsync(
        IUnitOfWork unitOfWork,
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    /// <summary>Deletes grant records inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the delete.</param>
    /// <param name="permissionGrants">The records to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">This storage cannot run inside a unit of work.</exception>
    Task DeleteManyEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable();
    }

    private static NotSupportedException _NotEnlistable()
    {
        return new NotSupportedException(
            "The configured permission grant repository cannot run inside a unit of work. Use the EF Core, PostgreSQL, "
                + "or SQL Server permissions storage, or write through the injected IPermissionManager."
        );
    }
}
