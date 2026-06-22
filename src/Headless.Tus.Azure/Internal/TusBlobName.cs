// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// Single source of truth for mapping a TUS file id to its Azure blob name. Shared by the store and the
/// Azure-lease lock provider so the two can never disagree on which blob a given file id resolves to.
/// </summary>
internal static class TusBlobName
{
    public static string Build(string blobPrefix, string fileId)
    {
        return $"{blobPrefix.EnsureEndsWith('/')}{fileId}";
    }
}
