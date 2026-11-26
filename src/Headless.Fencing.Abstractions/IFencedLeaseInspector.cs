// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// The read-only operator view of the fenced-lease table: list, look up, and count leases across tenants, and name the
/// kinds in use, for a dashboard or operator tooling.
/// </summary>
/// <remarks>
/// <para>
/// Every call is one lockless statement on the provider's own connection, with no transaction, so it never blocks a
/// grant, fence, or sweep. Expiry is judged by the database clock when the statement runs, never by the caller's clock.
/// On SQL Server a list or count skips a row another transaction holds locked, so it can miss a lease being written.
/// </para>
/// <para>
/// Unlike <see cref="IFencedLeases" />, the tenant is always explicit: nothing here reads the current tenant. Progress
/// bytes never leave the store; a <see cref="LeaseSnapshot" /> carries only their size and contract tag. The inspector
/// writes nothing. To delete ended leases, call <see cref="IFencedLeases.PurgeAsync" />.
/// </para>
/// </remarks>
[PublicAPI]
public interface IFencedLeaseInspector
{
    /// <summary>
    /// Returns at most <paramref name="pageSize" /> leases <paramref name="query" /> matches, ordered by tenant (the
    /// host scope first), kind, and resource, each ordinally, starting after <paramref name="after" />.
    /// </summary>
    /// <param name="query">The leases to read.</param>
    /// <param name="after">
    /// The <see cref="FencedLeasePage.Next" /> of the previous page, or <see langword="null" /> for the first page.
    /// </param>
    /// <param name="pageSize">The most leases to return, at most <see cref="FencedLeasePage.MaxPageSize" />.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The page, and the position of the next one when more leases match.</returns>
    /// <remarks>
    /// The keyset is the lease key, which no write changes, so paging never repeats or skips a lease that changes
    /// state between pages. A lease purged or granted between pages appears or disappears as it would in a fresh read.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A filter or the position names a tenant, kind, or resource no lease can have.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pageSize" /> is not between 1 and <see cref="FencedLeasePage.MaxPageSize" />.
    /// </exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException">The query's state is not a defined value.</exception>
    ValueTask<FencedLeasePage> ListAsync(
        FencedLeaseQuery query,
        FencedLeasePosition? after,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the attempt the lease row of <paramref name="tenantId" />, <paramref name="kind" />, and
    /// <paramref name="resource" /> holds, with its state by the database clock.
    /// </summary>
    /// <remarks>
    /// The same answer as <see cref="IFencedLeases.GetCurrentAsync(string, string, CancellationToken)" />, for a tenant
    /// the caller names instead of the current one. The answer can change as soon as it returns and never guards a
    /// write.
    /// </remarks>
    /// <param name="tenantId">The owning tenant, or <see langword="null" /> for the host scope.</param>
    /// <param name="kind">The lease kind.</param>
    /// <param name="resource">The leased resource within the kind.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The row's attempt, or <see langword="null" /> when the lease has no row (never granted, or purged).</returns>
    /// <exception cref="ArgumentException">The tenant id, kind, or resource is invalid.</exception>
    ValueTask<LeaseSnapshot?> GetAsync(
        string? tenantId,
        string kind,
        string resource,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns every lease kind with at least one row, of any tenant and state, ordered ordinally.</summary>
    /// <remarks>A kind whose every row was purged is no longer listed.</remarks>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The distinct kinds.</returns>
    ValueTask<IReadOnlyList<string>> ListKindsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the leases <paramref name="query" /> matches by state (live, expired, ended), each stopping at
    /// <paramref name="cap" />, in one statement judged on one database clock instant.
    /// </summary>
    /// <remarks>
    /// The query's <see cref="FencedLeaseQuery.State" /> keeps only that state's count; the others are zero. The store
    /// reads at most one lease past the cap per state, so a count costs no more than the cap.
    /// </remarks>
    /// <param name="query">The leases to count.</param>
    /// <param name="cap">The cap of each count.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>The capped counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A filter names a tenant, kind, or resource no lease can have.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap" /> is not positive.</exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException">The query's state is not a defined value.</exception>
    ValueTask<FencedLeaseCounts> CountAsync(
        FencedLeaseQuery query,
        int cap = LeaseCount.DefaultCap,
        CancellationToken cancellationToken = default
    );
}
