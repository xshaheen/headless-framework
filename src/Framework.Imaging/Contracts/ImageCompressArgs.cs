// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Imaging.Contracts;

public sealed class ImageCompressArgs(string? mimeType = null)
{
    public string? MimeType { get; private init; } = mimeType;
}
