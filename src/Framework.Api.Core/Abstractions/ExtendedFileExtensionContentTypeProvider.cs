using System.Diagnostics.CodeAnalysis;
using Framework.BuildingBlocks.Abstractions;
using Microsoft.AspNetCore.StaticFiles;

namespace Framework.Api.Core.Abstractions;

public sealed class ExtendedFileExtensionContentTypeProvider(IMimeTypeProvider mimeTypeProvider) : IContentTypeProvider
{
    public bool TryGetContentType(string subpath, [MaybeNullWhen(false)] out string contentType)
    {
        return mimeTypeProvider.TryGetMimeType(subpath, out contentType);
    }
}
