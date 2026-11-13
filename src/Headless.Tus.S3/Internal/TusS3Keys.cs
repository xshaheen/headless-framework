// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// Single source of truth for the object keys an upload owns, so every code path that turns a file id into a key
/// agrees on the result. One instance builds the keys of one tenant's uploads, or of the uploads kept outside any
/// tenant.
/// </summary>
/// <remarks>
/// <para>
/// A finished upload is <c>{prefix}{fileId}</c>. An incomplete upload's working set lives under
/// <c>{prefix}.tus/{fileId}/</c> and a completed upload's record at <c>{prefix}.tus-completed/{fileId}</c>, so the
/// expiry sweep lists only in-progress uploads and never pages through finished ones. A handed-off upload keeps a
/// mark at <c>{prefix}.tus-handed-off/{fileId}</c>.
/// </para>
/// <para>
/// A tenant's uploads have the same four keys with <c>.tenants/{tenant}/</c> in front of the file id:
/// <c>{prefix}.tenants/{tenant}/{fileId}</c>, <c>{prefix}.tus/.tenants/{tenant}/{fileId}/</c>, and so on. A file
/// id cannot start with a dot, so no upload kept outside a tenant can own a key inside that folder, and whatever
/// lists one of the four roots without a delimiter still reaches every tenant's objects under it.
/// </para>
/// </remarks>
internal sealed class TusS3Keys
{
    /// <summary>The folder under the prefix that holds every incomplete upload's working set.</summary>
    public const string WorkingSetFolder = ".tus/";

    /// <summary>The folder under the prefix that holds every completed upload's record.</summary>
    public const string CompletedFolder = ".tus-completed/";

    /// <summary>The folder under the prefix that holds the mark of every handed-off upload.</summary>
    public const string HandedOffFolder = ".tus-handed-off/";

    /// <summary>
    /// The folder, under the prefix and under each of the folders above, that holds one folder per tenant.
    /// </summary>
    public const string TenantsFolder = ".tenants/";

    private const string _InfoName = "info";
    private const string _SegmentFolder = "seg/";

    private readonly string _normalizedPrefix;
    private readonly string _completedRoot;
    private readonly string _handedOffRoot;

    /// <summary>Builds the keys of one tenant's uploads, or of the uploads kept outside any tenant.</summary>
    /// <param name="normalizedPrefix">The store's key prefix, ending in a slash.</param>
    /// <param name="tenant">
    /// The tenant whose uploads the keys belong to, already checked as a path segment, or <see langword="null"/>
    /// for the uploads kept outside any tenant.
    /// </param>
    public TusS3Keys(string normalizedPrefix, string? tenant = null)
    {
        var scope = tenant is null ? "" : TenantsFolder + tenant + "/";

        _normalizedPrefix = normalizedPrefix;
        Tenant = tenant;
        FinishedObjectRoot = normalizedPrefix + scope;
        WorkingSetRoot = normalizedPrefix + WorkingSetFolder + scope;
        _completedRoot = normalizedPrefix + CompletedFolder + scope;
        _handedOffRoot = normalizedPrefix + HandedOffFolder + scope;
    }

    /// <summary>
    /// The tenant whose uploads the keys belong to, or <see langword="null"/> for the uploads kept outside any
    /// tenant.
    /// </summary>
    public string? Tenant { get; }

    /// <summary>The keys of <paramref name="tenant"/>'s uploads under the same prefix.</summary>
    /// <param name="tenant">The tenant id, already checked as a path segment.</param>
    public TusS3Keys ForTenant(string tenant)
    {
        return new TusS3Keys(_normalizedPrefix, tenant);
    }

    /// <summary>The prefix every finished object of these uploads starts with.</summary>
    public string FinishedObjectRoot { get; }

    /// <summary>The key of the uploaded file once the upload completes.</summary>
    public string FinishedObject(string fileId)
    {
        return FinishedObjectRoot + EnsureValid(fileId);
    }

    /// <summary>The prefix every incomplete upload's working set lives under, one folder per upload.</summary>
    public string WorkingSetRoot { get; }

    /// <summary>
    /// The prefix under which every tenant with an incomplete upload has a folder, beside the working sets of the
    /// uploads kept outside any tenant.
    /// </summary>
    public string TenantWorkingSetsRoot => _normalizedPrefix + WorkingSetFolder + TenantsFolder;

    /// <summary>The prefix every object of an incomplete upload's working set starts with.</summary>
    public string WorkingSet(string fileId)
    {
        return WorkingSetRoot + EnsureValid(fileId) + "/";
    }

    /// <summary>The key of an incomplete upload's state object.</summary>
    public string Info(string fileId)
    {
        return WorkingSet(fileId) + _InfoName;
    }

    /// <summary>The prefix an incomplete upload's tail segments start with.</summary>
    public string Segments(string fileId)
    {
        return WorkingSet(fileId) + _SegmentFolder;
    }

    /// <summary>
    /// The key of a tail segment holding bytes from <paramref name="offset"/>. The nonce makes every write a new key,
    /// so a writer whose view is stale can never overwrite a segment another request committed.
    /// </summary>
    public string Segment(string fileId, long offset, string nonce)
    {
        return Segments(fileId) + offset.ToString("D19", CultureInfo.InvariantCulture) + "-" + nonce;
    }

    /// <summary>The key of the record a completed upload keeps beside its finished object.</summary>
    public string CompletedRecord(string fileId)
    {
        return _completedRoot + EnsureValid(fileId);
    }

    /// <summary>
    /// The key of the mark a completed upload gets once it was handed off to the application. It is an object of
    /// its own because a completion that runs again rewrites the completed record, which would erase a mark kept
    /// inside it.
    /// </summary>
    public string HandedOffMark(string fileId)
    {
        return _handedOffRoot + EnsureValid(fileId);
    }

    /// <summary>
    /// Returns <paramref name="fileId"/> when it is safe to place in a key, otherwise throws.
    /// </summary>
    /// <exception cref="TusStoreException">The id could address an object outside the upload's own keys.</exception>
    public static string EnsureValid(string? fileId)
    {
        // The id becomes part of a key, so reject ids that could reach another upload's objects or the store's
        // reserved folders. The default GUID ids never fail this; a crafted concatenation member id or a custom
        // ITusFileIdProvider could.
        if (string.IsNullOrEmpty(fileId) || _IsUnsafe(fileId))
        {
            throw new TusStoreException($"Invalid TUS file id: '{fileId}'.");
        }

        return fileId;
    }

    /// <summary>Whether <paramref name="fileId"/> is safe to place in a key.</summary>
    public static bool IsValid([NotNullWhen(true)] string? fileId)
    {
        return !string.IsNullOrEmpty(fileId) && !_IsUnsafe(fileId);
    }

    private static bool _IsUnsafe(string fileId)
    {
        // A leading dot would let an id name one of the store's reserved folders, which all start with one; it
        // also covers "." and "..", which some S3-compatible servers normalize out of a path.
        if (fileId[0] == '.')
        {
            return true;
        }

        if (char.IsWhiteSpace(fileId[0]) || char.IsWhiteSpace(fileId[^1]))
        {
            return true;
        }

        foreach (var c in fileId)
        {
            // A separator would nest the id inside another upload's keys or the reserved folders.
            if (c is '/' or '\\' || char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
