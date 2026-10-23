// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.IpGeolocation.Internal;

/// <summary>
/// Downloads the databases a fresh host lacks before it reports ready, so a new instance does not answer lookups with
/// empty results while its first download runs.
/// </summary>
/// <remarks>
/// It gates readiness only when downloads are enabled and an edition had no readable file at startup. With every file
/// present the host serves from them at once and the updater checks for newer releases as usual; without credentials
/// nothing can be downloaded, so a missing file stays a startup warning.
/// </remarks>
internal sealed class MaxMindDatabaseWarmup(MaxMindDatabaseSync sync) : IWarmup
{
    public string Name => "MaxMindDatabases";

    public bool GatesReadiness => sync.NeedsInitialDownload;

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (!sync.NeedsInitialDownload)
        {
            return;
        }

        // Every edition, not only the missing ones: the updater skips its own first check because this one ran.
        var failures = await sync.UpdateAllAsync(cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            throw MaxMindDatabaseSync.ToException(failures);
        }
    }
}
