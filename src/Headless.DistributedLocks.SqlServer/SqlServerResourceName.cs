// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// Encodes logical resource names into SQL Server's <c>sp_getapplock @Resource</c> length budget
/// (<see cref="SqlServerDistributedLockFieldLimits.MaxResourceNameLength"/> characters).
/// </summary>
/// <remarks>
/// Names that fit within the 255-character limit are returned unchanged. Names that exceed the limit are
/// replaced by a stable <c>sha256:</c>-prefixed hex digest of the UTF-8 encoded name, which always fits.
/// The encoding is deterministic across processes: two callers with the same logical name always produce
/// the same encoded resource, so they mutually exclude on the same application lock.
/// </remarks>
public static class SqlServerResourceName
{
    private const string _HashPrefix = "sha256:";

    /// <summary>
    /// Returns an encoded form of <paramref name="resource"/> that fits within SQL Server's
    /// 255-character <c>@Resource</c> limit. If <paramref name="resource"/> is within the limit it is
    /// returned as-is; otherwise it is replaced by a <c>sha256:</c>-prefixed lowercase hex digest of its
    /// UTF-8 encoding.
    /// </summary>
    /// <param name="resource">The logical resource name to encode. Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <returns>
    /// The original <paramref name="resource"/> string when its length is at or below
    /// <see cref="SqlServerDistributedLockFieldLimits.MaxResourceNameLength"/>; otherwise a
    /// <c>sha256:</c>-prefixed lowercase hex string derived from the resource's UTF-8 bytes.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or consists only of whitespace.</exception>
    public static string Encode(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        if (resource.Length <= SqlServerDistributedLockFieldLimits.MaxResourceNameLength)
        {
            return resource;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));

        return _HashPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
