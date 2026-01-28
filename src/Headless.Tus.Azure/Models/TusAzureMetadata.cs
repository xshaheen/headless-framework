// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Headless.Tus.Models;

internal sealed partial class TusAzureMetadata
{
    public const string UploadLengthKey = "tus_upload_length";
    public const string ExpirationKey = "tus_expiration";
    public const string CreatedDateKey = "tus_created";
    public const string ConcatTypeKey = "tus_concat_type";
    public const string PartialUploadsKey = "tus_partial_uploads";
    public const string FileNameKey = "tus_filename";
    public const string LastChunkBlocksKey = "tus_last_chunk_blocks";
    public const string LastChunkChecksumKey = "tus_last_chunk_checksum";

    private TusAzureMetadata(IDictionary<string, string> decodedMetadata)
    {
        _decodedMetadata = decodedMetadata;
    }

    private readonly IDictionary<string, string> _decodedMetadata;

    public string? Filename
    {
        get => _decodedMetadata.TryGetValue(FileNameKey, out var value) ? value : null;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _decodedMetadata.Remove(FileNameKey);
            }
            else
            {
                _decodedMetadata[FileNameKey] = value;
            }
        }
    }

    public DateTimeOffset? DateCreated
    {
        get
        {
            return
                _decodedMetadata.TryGetValue(CreatedDateKey, out var value) && _ParseDateTimeOffset(value, out var date)
                ? date
                : null;
        }
        set
        {
            if (value.HasValue)
            {
                _decodedMetadata[CreatedDateKey] = value.Value.ToString("O");
            }
            else
            {
                _decodedMetadata.Remove(CreatedDateKey);
            }
        }
    }

    public DateTimeOffset? DateExpiration
    {
        get =>
            _decodedMetadata.TryGetValue(ExpirationKey, out var value) && _ParseDateTimeOffset(value, out var date)
                ? date
                : null;
        set
        {
            if (value.HasValue)
            {
                _decodedMetadata[ExpirationKey] = value.Value.ToString("O");
            }
            else
            {
                _decodedMetadata.Remove(ExpirationKey);
            }
        }
    }

    public long? UploadLength
    {
        get =>
            _decodedMetadata.TryGetValue(UploadLengthKey, out var value)
            && long.TryParse(value, CultureInfo.InvariantCulture, out var length)
                ? length
                : 0;
        set
        {
            if (value.HasValue)
            {
                _decodedMetadata[UploadLengthKey] = value.Value.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                _decodedMetadata.Remove(UploadLengthKey);
            }
        }
    }

    public string[]? LastChunkBlocks
    {
        get =>
            _decodedMetadata.TryGetValue(LastChunkBlocksKey, out var value) && !string.IsNullOrEmpty(value)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
        set
        {
            if (value == null || value.Length == 0)
            {
                _decodedMetadata.Remove(LastChunkBlocksKey);
            }
            else
            {
                _decodedMetadata[LastChunkBlocksKey] = string.Join(',', value);
            }
        }
    }

    public string? LastChunkChecksum
    {
        get => _decodedMetadata.TryGetValue(LastChunkChecksumKey, out var value) ? value : null;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _decodedMetadata.Remove(LastChunkChecksumKey);
            }
            else
            {
                _decodedMetadata[LastChunkChecksumKey] = value;
            }
        }
    }

    public string? ConcatType
    {
        get => _decodedMetadata.TryGetValue(ConcatTypeKey, out var value) ? value : null;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _decodedMetadata.Remove(ConcatTypeKey);
            }
            else
            {
                _decodedMetadata[ConcatTypeKey] = value;
            }
        }
    }

    public string[]? PartialUploads
    {
        get =>
            _decodedMetadata.TryGetValue(PartialUploadsKey, out var value)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;
        set
        {
            if (value == null || value.Length == 0)
            {
                _decodedMetadata.Remove(PartialUploadsKey);
            }
            else
            {
                _decodedMetadata[PartialUploadsKey] = string.Join(',', value);
            }
        }
    }

    #region From/To Converters

    public IDictionary<string, string> ToAzure() => _decodedMetadata;

    public static TusAzureMetadata FromAzure(IDictionary<string, string> metadata) => new(metadata);

    public Dictionary<string, string> ToUser()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in _decodedMetadata)
        {
            if (_IsSystemMetadataKey(key))
            {
                continue; // Skip non-tus metadata and system keys
            }

            result[key] = value;
        }

        return result;
    }

    public string ToTusString()
    {
        if (_decodedMetadata.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (var (key, value) in _decodedMetadata)
        {
            if (_IsSystemMetadataKey(key))
            {
                continue; // Skip non-tus metadata and system keys
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(CultureInfo.InvariantCulture, $"{key} {value.ToBase64()}");
        }

        return sb.ToString();
    }

    public Dictionary<string, Metadata> ToTus()
    {
        var text = ToTusString();
        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, text);

        return parseResult.Metadata;
    }

    public static TusAzureMetadata FromTus(string? metadata)
    {
        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, metadata);

        if (!parseResult.Success)
        {
            throw new TusStoreException(parseResult.ErrorMessage);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in parseResult.Metadata)
        {
            result[_SanitizeAzureMetadataKey(key)] = value.GetString(Encoding.UTF8);
        }

        return new TusAzureMetadata(result);
    }

    #endregion

    #region Azure Key Sanitization

    private static string _SanitizeAzureMetadataKey(string key)
    {
        // Azure metadata keys: must start with letter or underscore, alphanumeric and underscore only
        var sanitized = _AzureMetadataKey().Replace(key, "_").ToLowerInvariant();

        // Ensure it starts with a letter or underscore
        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]", RegexOptions.None, 100)]
    private static partial Regex _AzureMetadataKey();

    #endregion

    #region Helpers

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

    private static bool _IsSystemMetadataKey(string key)
    {
        return key
            is UploadLengthKey
                or ExpirationKey
                or CreatedDateKey
                or ConcatTypeKey
                or PartialUploadsKey
                or LastChunkBlocksKey
                or LastChunkChecksumKey;
    }

    #endregion
}
