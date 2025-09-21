// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore
{
    private const string _TusMetadataPrefix = "tus_";
    private const string _UploadLengthKey = "tus_upload_length";
    private const string _ExpirationKey = "tus_expiration";
    private const string _CreatedDateKey = "tus_created";
    private const string _BlockCountKey = "tus_block_count";

    private static void _SetCreatedDate(Dictionary<string, string> metadata, DateTimeOffset createdDate)
    {
        metadata[_CreatedDateKey] = createdDate.ToString("O");
    }

    private static DateTimeOffset _GetCreatedDate(Dictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_CreatedDateKey, out var value) && _ParseDateTimeOffset(value, out var date)
            ? date
            : DateTimeOffset.UtcNow;
    }

    private static void _SetUploadLength(Dictionary<string, string> metadata, long? uploadLength)
    {
        if (uploadLength.HasValue)
        {
            metadata[_UploadLengthKey] = uploadLength.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            metadata.Remove(_UploadLengthKey);
        }
    }

    private static long? _GetUploadLength(Dictionary<string, string> metadata)
    {
        return
            metadata.TryGetValue(_UploadLengthKey, out var value)
            && long.TryParse(value, CultureInfo.InvariantCulture, out var length)
            ? length
            : null;
    }

    private static DateTimeOffset? _GetExpirationDate(IDictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_ExpirationKey, out var value) && _ParseDateTimeOffset(value, out var date)
            ? date
            : null;
    }

    private static void _SetExpirationDate(Dictionary<string, string> metadata, DateTimeOffset? expirationDate)
    {
        if (expirationDate.HasValue)
        {
            metadata[_ExpirationKey] = expirationDate.Value.ToString("O");
        }
        else
        {
            metadata.Remove(_ExpirationKey);
        }
    }

    private static bool _ParseDateTimeOffset(string value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParseExact(
            value,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out date
        );
    }

    private static void _SetBlockCount(Dictionary<string, string> metadata, int blockCount)
    {
        metadata[_BlockCountKey] = blockCount.ToString(CultureInfo.InvariantCulture);
    }

    private static int _GetBlockCount(Dictionary<string, string> metadata)
    {
        return
            metadata.TryGetValue(_BlockCountKey, out var value)
            && int.TryParse(value, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    private static bool _IsSystemMetadataKey(string key)
    {
        return key is _UploadLengthKey or _ExpirationKey or _CreatedDateKey or _BlockCountKey;
    }
}
