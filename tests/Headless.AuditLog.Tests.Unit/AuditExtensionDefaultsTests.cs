// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Tests;

/// <summary>
/// The default an external <see cref="IAuditEntityIdResolver"/> inherits when it does not override the newer member:
/// the conservative two-phase flow.
/// </summary>
public sealed class AuditExtensionDefaultsTests
{
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

    private sealed class DefaultResolver : IAuditEntityIdResolver
    {
        public void ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries) { }
    }
}
