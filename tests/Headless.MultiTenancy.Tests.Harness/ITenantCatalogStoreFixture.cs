// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Tests;

/// <summary>
/// Provider-neutral contract for a tenant-store conformance fixture. Each leaf fixture owns its own
/// backing resource (nothing for the in-memory/configuration stores; a Testcontainers database for the
/// EF store) and implements <see cref="SeedAsync"/> the way its store actually gets data: the in-memory
/// and configuration stores build a fresh DI host from seed options (duplicates fail when the host
/// starts, per R20's fail-fast-at-startup contract); the EF store inserts <c>TenantRecord</c> rows
/// directly against the container database (duplicates fail on the unique-index violation, per KTD6).
/// Whichever the mechanism, a seed set with a duplicate normalized identifier must fail
/// <see cref="SeedAsync"/>, and a valid seed set must return a store that also implements
/// <see cref="ITenantDirectory"/> — all v1 stores enumerate (R4).
/// </summary>
public interface ITenantCatalogStoreFixture
{
    /// <summary>
    /// Builds/seeds the store with <paramref name="seeds"/> and returns the resulting <see cref="ITenantStore"/>.
    /// Each call is independent: a leaf fixture that reuses one external resource (a Testcontainers
    /// database) clears prior tenant rows before applying the new seed set, so tests in the same class can
    /// call this repeatedly with different seed sets.
    /// </summary>
    /// <param name="seeds">Already-normalized tenant seed data — the store must not re-normalize (R7).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="ITenantStore"/> that also implements <see cref="ITenantDirectory"/>.</returns>
    /// <exception cref="Exception">
    /// Two or more seeds normalize to the same identifier (R20). The concrete exception type is
    /// provider-specific — in-memory/configuration: an options-validation failure raised when the backing
    /// host starts; EF: a unique-index-violation <c>DbUpdateException</c> raised on insert.
    /// </exception>
    Task<ITenantStore> SeedAsync(IReadOnlyList<TenantSeed> seeds, CancellationToken cancellationToken);
}
