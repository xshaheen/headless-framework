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
