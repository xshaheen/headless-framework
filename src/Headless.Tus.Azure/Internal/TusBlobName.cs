// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// Single source of truth for mapping a TUS file id to its Azure blob name, so every code path that
/// resolves a file id to a blob agrees on the result.
/// </summary>
internal static class TusBlobName
{
    public static string Build(string blobPrefix, string fileId)
    {
        return $"{blobPrefix.EnsureEndsWith('/')}{fileId}";
    }
}
