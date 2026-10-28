// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// Single source of truth for the object keys an upload owns, so every code path that turns a file id into a key
/// agrees on the result.
/// </summary>
/// <remarks>
/// A finished upload is <c>{prefix}{fileId}</c>. An incomplete upload's working set lives under
/// <c>{prefix}.tus/{fileId}/</c> and a completed upload's record at <c>{prefix}.tus-completed/{fileId}</c>, so the
/// expiry sweep lists only in-progress uploads and never pages through finished ones.
/// </remarks>
internal sealed class TusS3Keys(string normalizedPrefix)
{
    private const string _InfoName = "info";
    private const string _SegmentFolder = "seg/";

    /// <summary>The key of the uploaded file once the upload completes.</summary>
    public string FinishedObject(string fileId)
    {
        return normalizedPrefix + EnsureValid(fileId);
    }

    /// <summary>The prefix every incomplete upload's working set lives under, one folder per upload.</summary>
    public string WorkingSetRoot => normalizedPrefix + TusS3BucketProvisioner.WorkingSetFolder;

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
        return normalizedPrefix + TusS3BucketProvisioner.CompletedFolder + EnsureValid(fileId);
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
        // A leading dot would let an id name the reserved .tus/ or .tus-completed/ folders; it also covers "." and
        // "..", which some S3-compatible servers normalize out of a path.
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
