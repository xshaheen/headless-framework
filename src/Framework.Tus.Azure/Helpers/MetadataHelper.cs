// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;
using Framework.Tus.Models;

namespace Framework.Tus.Helpers;

public static class MetadataHelper
{
    private const string _TusMetadataPrefix = "tus_";
    private const string _UploadLengthKey = "tus_upload_length";
    private const string _ExpirationKey = "tus_expiration";
    private const string _CreatedDateKey = "tus_created";
    private const string _BlobTypeKey = "tus_blob_type";
    private const string _BlockCountKey = "tus_block_count";

    public static Dictionary<string, string> EncodeTusMetadata(Dictionary<string, string> tusMetadata)
    {
        var blobMetadata = new Dictionary<string, string>();

        foreach (var kvp in tusMetadata)
        {
            var sanitizedKey = _SanitizeMetadataKey($"{_TusMetadataPrefix}{kvp.Key}");
            var encodedValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(kvp.Value));
            blobMetadata[sanitizedKey] = encodedValue;
        }

        return blobMetadata;
    }

    public static Dictionary<string, string> DecodeTusMetadata(Dictionary<string, string> blobMetadata)
    {
        var tusMetadata = new Dictionary<string, string>();

        foreach (var kvp in blobMetadata)
        {
            if (kvp.Key.StartsWith(_TusMetadataPrefix) && !_IsSystemMetadataKey(kvp.Key))
            {
                try
                {
                    var originalKey = kvp.Key[_TusMetadataPrefix.Length..];
                    var decodedValue = Encoding.UTF8.GetString(Convert.FromBase64String(kvp.Value));
                    tusMetadata[originalKey] = decodedValue;
                }
                catch (FormatException)
                {
                    // Skip invalid base64 values
                }
            }
        }

        return tusMetadata;
    }

    public static void SetUploadLength(Dictionary<string, string> metadata, long? uploadLength)
    {
        if (uploadLength.HasValue)
            metadata[_UploadLengthKey] = uploadLength.Value.ToString();
        else
            metadata.Remove(_UploadLengthKey);
    }

    public static long? GetUploadLength(Dictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_UploadLengthKey, out var value) && long.TryParse(value, out var length)
            ? length
            : null;
    }

    public static void SetExpirationDate(Dictionary<string, string> metadata, DateTimeOffset? expirationDate)
    {
        if (expirationDate.HasValue)
            metadata[_ExpirationKey] = expirationDate.Value.ToString("O");
        else
            metadata.Remove(_ExpirationKey);
    }

    public static DateTimeOffset? GetExpirationDate(Dictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_ExpirationKey, out var value) && DateTimeOffset.TryParse(value, out var date)
            ? date
            : null;
    }

    public static void SetCreatedDate(Dictionary<string, string> metadata, DateTimeOffset createdDate)
    {
        metadata[_CreatedDateKey] = createdDate.ToString("O");
    }

    public static DateTimeOffset GetCreatedDate(Dictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_CreatedDateKey, out var value) && DateTimeOffset.TryParse(value, out var date)
            ? date
            : DateTimeOffset.UtcNow;
    }

    public static void SetBlobType(Dictionary<string, string> metadata, BlobType blobType)
    {
        metadata[_BlobTypeKey] = blobType.ToString();
    }

    public static BlobType GetBlobType(Dictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_BlobTypeKey, out var value) && Enum.TryParse<BlobType>(value, out var blobType)
            ? blobType
            : BlobType.BlockBlob;
    }

    public static void SetBlockCount(Dictionary<string, string> metadata, int blockCount)
    {
        metadata[_BlockCountKey] = blockCount.ToString();
    }

    public static int GetBlockCount(Dictionary<string, string> metadata)
    {
        return metadata.TryGetValue(_BlockCountKey, out var value) && int.TryParse(value, out var count) ? count : 0;
    }

    private static bool _IsSystemMetadataKey(string key)
    {
        return key == _UploadLengthKey
            || key == _ExpirationKey
            || key == _CreatedDateKey
            || key == _BlobTypeKey
            || key == _BlockCountKey;
    }

    private static string _SanitizeMetadataKey(string key)
    {
        // Azure metadata keys: must start with letter or underscore, alphanumeric and underscore only
        var sanitized = Regex.Replace(key, @"[^a-zA-Z0-9_]", "_").ToLowerInvariant();

        // Ensure it starts with a letter or underscore
        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            sanitized = "_" + sanitized;

        return sanitized;
    }
}
