using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

[PublicAPI]
public static class StringHelper
{
    public static Encoding Utf8WithoutBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Converts a byte[] to string without BOM (byte order mark).</summary>
    /// <param name="bytes">The byte[] to be converted to string</param>
    /// <param name="encoding">The encoding to get string. Default is UTF8</param>
    [return: NotNullIfNotNull(nameof(bytes))]
    public static string? ConvertFromBytesWithoutBom(byte[]? bytes, Encoding? encoding = null)
    {
        if (bytes is null)
        {
            return null;
        }

        encoding ??= Encoding.UTF8;

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        return hasBom ? encoding.GetString(bytes, 3, bytes.Length - 3) : encoding.GetString(bytes);
    }
}
