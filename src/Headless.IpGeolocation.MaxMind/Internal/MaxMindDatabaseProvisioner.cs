// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Hosting;
using MaxMind.Db;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation.Internal;

/// <summary>
/// Offers the database files as a provisioned resource, so an image build or a deploy step can download them with
/// <c>resources provision -k MaxMind</c> and a new host starts with them on disk.
/// </summary>
internal sealed class MaxMindDatabaseProvisioner(
    MaxMindDatabases databases,
    MaxMindDatabaseSync sync,
    IOptions<MaxMindOptions> options
) : IResourceProvisioner
{
    private readonly MaxMindOptions _options = options.Value;

    public string Kind => "MaxMind";

    public string Name => "databases";

    // The files are the only data, so there is nothing to purge apart from deleting them.
    public bool CanPurge => false;

    // Without credentials the files are the application's own to place, so there is nothing to download. An operator
    // runs provision on purpose, so it tries a download that failed recently again instead of waiting out the retry delay.
    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        if (!_options.HasCredentials)
        {
            return;
        }

        var failures = await sync.UpdateAllNowAsync(cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            throw MaxMindDatabaseSync.ToException(failures);
        }
    }

    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var unreadable = new List<string>();

        foreach (var editionId in databases.Editions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = databases.PathOf(editionId);

            if (!File.Exists(path))
            {
                missing.Add(editionId);
            }
            else if (databases.Get(editionId) is null && !_CanOpen(path))
            {
                unreadable.Add(editionId);
            }
        }

        if (missing.Count == 0 && unreadable.Count == 0)
        {
            return Task.CompletedTask;
        }

        var problems = new List<string>(2);

        if (missing.Count > 0)
        {
            problems.Add($"missing: {string.Join(", ", missing)}");
        }

        if (unreadable.Count > 0)
        {
            problems.Add($"not a readable MaxMind database: {string.Join(", ", unreadable)}");
        }

        var fix = _options.HasCredentials
            ? "Run 'resources provision -k MaxMind' to download them."
            : "Place each one in the database directory as <EditionId>.mmdb, or set AccountId and LicenseKey to download them.";

        throw new ResourceProvisioningException($"MaxMind databases {string.Join("; ", problems)}. {fix}");
    }

    public Task PurgeAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "The MaxMind databases hold no data apart from the files themselves; run 'resources delete' to delete them."
        );

    public Task DeleteAsync(CancellationToken cancellationToken) => sync.DeleteAllAsync(cancellationToken);

    public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<KeyValuePair<string, string>> status =
        [
            .. databases.Editions.Select(editionId => new KeyValuePair<string, string>(
                editionId,
                databases.Get(editionId) is { } database
                    ? "built " + database.BuildDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "missing"
            )),
            new("downloads", _options.HasCredentials ? "enabled" : "disabled"),
        ];

        return Task.FromResult(status);
    }

    private bool _CanOpen(string path)
    {
        try
        {
            databases.Open(path).Reader.Dispose();

            return true;
        }
        catch (Exception e) when (e is InvalidDatabaseException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
