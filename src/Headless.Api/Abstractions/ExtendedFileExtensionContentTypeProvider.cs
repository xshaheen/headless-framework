// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Abstractions;
using Microsoft.AspNetCore.StaticFiles;

namespace Headless.Api.Abstractions;

public sealed class ExtendedFileExtensionContentTypeProvider(IMimeTypeProvider mimeTypeProvider) : IContentTypeProvider
{
    public bool TryGetContentType(string subpath, [MaybeNullWhen(false)] out string contentType)
    {
        return mimeTypeProvider.TryGetMimeType(subpath, out contentType);
    }
}
