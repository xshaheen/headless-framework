// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Settings.Definitions;
using Microsoft.Extensions.Logging;

namespace Headless.Settings.Values;

/// <summary>The type-erased side of a registered snapshot that the consumer and the hosted service drive.</summary>
internal interface ISettingsSnapshotEntry
{
    /// <summary>The setting names the snapshot is bound from.</summary>
    IReadOnlySet<string> Names { get; }

    /// <summary>The backstop re-read interval.</summary>
    TimeSpan Backstop { get; }

    /// <summary>Whether the first load has completed.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Validates the names and performs the first load unless it already completed. Throws when either fails, so the
    /// caller decides whether to retry.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads a loaded snapshot. Does nothing until the first load completes: that load owns name validation, and a value
    /// nobody read yet has no missed change to recover. Failures are logged, never thrown, except cancellation.
    /// </summary>
    /// <param name="reason">Why the reload runs.</param>
    /// <param name="announcedNames">The tracked names a message announced, for <see cref="SettingsSnapshotReloadReason.Message"/>.</param>
    /// <param name="cancellationToken">The abort token.</param>
    Task ReloadAsync(
        SettingsSnapshotReloadReason reason,
        IReadOnlyCollection<string>? announcedNames,
        CancellationToken cancellationToken
    );
}
