// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Tests;

/// <summary>
/// The defaults an external <see cref="IAuditLogStore"/> or <see cref="IAuditEntityIdResolver"/> inherits when it
/// does not override the newer members: the conservative two-phase flow.
/// </summary>
public sealed class AuditExtensionDefaultsTests
{
    [Fact]
    public void should_not_add_to_the_saving_context_by_default()
    {
        // given
        IAuditLogStore store = new DefaultStore();

        // when / then
        store.AddsToSavingContext.Should().BeFalse();
    }

    [Fact]
    public void should_require_deferred_resolution_by_default()
    {
        // given
        IAuditEntityIdResolver resolver = new DefaultResolver();

        // when / then
        resolver
            .RequiresDeferredResolution(new AuditLogEntryData { Action = "a", CreatedAt = DateTimeOffset.UnixEpoch })
            .Should()
            .BeTrue();
    }

    private sealed class DefaultStore : IAuditLogStore
    {
        public IReadOnlyList<IAuditLogStoreEntry> Save(
            IReadOnlyList<AuditLogEntryData> entries,
            object savingContext
        ) => [];

        public Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
            IReadOnlyList<AuditLogEntryData> entries,
            object savingContext,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<IAuditLogStoreEntry>>([]);
    }

    private sealed class DefaultResolver : IAuditEntityIdResolver
    {
        public void ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries) { }
    }
}
