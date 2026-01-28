// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.Imaging;

public sealed class ImageCompressArgs(string? mimeType = null)
{
    public string? MimeType { get; private init; } = mimeType;
}
