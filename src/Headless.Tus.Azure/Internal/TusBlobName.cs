// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// Single source of truth for mapping a TUS file id to its Azure blob name and back, so every code path that
/// resolves a file id to a blob agrees on the result.
/// </summary>
/// <remarks>
/// An upload kept outside any tenant is <c>{blobPrefix}/{fileId}</c>, and a tenant's upload is
/// <c>{blobPrefix}/.tenants/{tenant}/{fileId}</c>. A file id cannot contain a slash or start with a dot, so no
/// upload kept outside a tenant can own a name inside that folder, and whatever lists the prefix without a
/// delimiter still reaches every tenant's blobs under it.
/// </remarks>
internal static class TusBlobName
{
    /// <summary>The folder under the blob prefix that holds one folder per tenant.</summary>
    public const string TenantsFolder = ".tenants/";

    /// <summary>Builds the name of the blob that holds an upload.</summary>
    /// <param name="blobPrefix">The store's blob prefix, with or without its trailing slash.</param>
    /// <param name="fileId">The TUS file identifier.</param>
    /// <param name="tenant">
    /// The tenant the upload belongs to, already checked as a path segment, or <see langword="null"/> for an
    /// upload kept outside any tenant.
    /// </param>
    /// <exception cref="TusStoreException">The id could address a blob other than the upload's own.</exception>
    public static string Build(string blobPrefix, string fileId, string? tenant = null)
    {
        // Defense-in-depth: the file id becomes part of the blob name, so reject ids that could address a
        // blob outside the intended prefix. Legitimate ids (the GUID provider; a single URL path segment)
        // never contain these — a crafted concatenation member id or a custom ITusFileIdProvider could.
        if (string.IsNullOrEmpty(fileId) || _IsUnsafe(fileId))
        {
            throw new TusStoreException($"Invalid TUS file id: '{fileId}'.");
        }

        var root = blobPrefix.EnsureEndsWith('/');

        return tenant is null ? root + fileId : root + TenantsFolder + tenant + "/" + fileId;
    }

    /// <summary>
    /// Splits a blob name listed under <paramref name="blobPrefix"/> into the tenant and the file id
    /// <see cref="Build"/> made it from.
    /// </summary>
    /// <param name="blobPrefix">The store's blob prefix, with or without its trailing slash.</param>
    /// <param name="blobName">The full blob name.</param>
    /// <param name="tenant">
    /// The tenant the upload belongs to, or <see langword="null"/> for an upload kept outside any tenant.
    /// </param>
    /// <param name="fileId">The TUS file identifier.</param>
    /// <returns>
    /// <see langword="false"/> when <see cref="Build"/> could not have produced the name, so the blob is not an
    /// upload of this store.
    /// </returns>
    public static bool TrySplit(
        string blobPrefix,
        string blobName,
        out string? tenant,
        [NotNullWhen(true)] out string? fileId
    )
    {
        tenant = null;
        fileId = null;

        var root = blobPrefix.EnsureEndsWith('/');

        if (!blobName.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = blobName[root.Length..];
        string? folder = null;

        if (rest.StartsWith(TenantsFolder, StringComparison.Ordinal))
        {
            var scoped = rest[TenantsFolder.Length..];
            var separator = scoped.IndexOf('/', StringComparison.Ordinal);

            // The store writes under a tenant only after this same check, so any other folder is not its own.
            if (separator < 0 || !TenantStorageSegment.IsValid(scoped[..separator]))
            {
                return false;
            }

            folder = scoped[..separator];
            rest = scoped[(separator + 1)..];
        }

        if (rest.Length == 0 || _IsUnsafe(rest))
        {
            return false;
        }

        tenant = folder;
        fileId = rest;

        return true;
    }

    private static bool _IsUnsafe(string fileId)
    {
        // A leading dot would let an id name the folder that holds the tenants, which starts with one.
        if (fileId[0] == '.' || fileId.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        // Leading/trailing whitespace corrupts the comma-joined round-trips below (they split with
        // TrimEntries) and trailing spaces/dots are hazardous in Azure blob names.
        if (char.IsWhiteSpace(fileId[0]) || char.IsWhiteSpace(fileId[^1]))
        {
            return true;
        }

        foreach (var c in fileId)
        {
            // ',' is the separator used to persist file-id lists in blob metadata
            // (tus_partial_uploads); an id containing one would corrupt the round-trip and make
            // GetUploadConcatAsync report the wrong constituent files.
            if (c is '/' or '\\' or ',' || char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
