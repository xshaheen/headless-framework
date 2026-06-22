// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// Single source of truth for mapping a TUS file id to its Azure blob name, so every code path that
/// resolves a file id to a blob agrees on the result.
/// </summary>
internal static class TusBlobName
{
    public static string Build(string blobPrefix, string fileId)
    {
        // Defense-in-depth: the file id becomes part of the blob name, so reject ids that could address a
        // blob outside the intended prefix. Legitimate ids (the GUID provider; a single URL path segment)
        // never contain these — a crafted concatenation member id or a custom ITusFileIdProvider could.
        if (string.IsNullOrEmpty(fileId) || _IsUnsafe(fileId))
        {
            throw new TusStoreException($"Invalid TUS file id: '{fileId}'.");
        }

        return $"{blobPrefix.EnsureEndsWith('/')}{fileId}";
    }

    private static bool _IsUnsafe(string fileId)
    {
        if (fileId.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var c in fileId)
        {
            if (c is '/' or '\\' || char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
