// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps.Internals;

internal static class StringHelper
{
    internal static Encoding Utf8WithoutBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
