// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Tus.Options;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Framework.Tus.Models;

public sealed partial class TusAzureMetadata
{
    public const string TusMetadataPrefix = "tus_";
    public const string UploadLengthKey = "tus_upload_length";
    public const string ExpirationKey = "tus_expiration";
    public const string CreatedDateKey = "tus_created";
    public const string BlockCountKey = "tus_block_count";
    public const string ConcatTypeKey = "tus_concat_type";
    public const string PartialUploadsKey = "tus_partial_uploads";

    private TusAzureMetadata(IDictionary<string, string> decodedMetadata)
    {
        _decodedMetadata = decodedMetadata;
    }

    private readonly IDictionary<string, string> _decodedMetadata;

    public DateTimeOffset DateCreated
    {
        get => _GetDateCreated();
        set => _SetDateCreated(value);
    }

    public DateTimeOffset? DateExpiration
    {
        get => _GetExpirationDate();
        set => _SetExpirationDate(value);
    }

    public long? UploadLength
    {
        get => _GetUploadLength();
        set => _SetUploadLength(value);
    }

    public int BlockCount
    {
        get => _GetBlockCount();
        set => _SetBlockCount(value);
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

    #region Read/Write Helpers

    private DateTimeOffset _GetDateCreated()
    {
        return _decodedMetadata.TryGetValue(CreatedDateKey, out var value) && _ParseDateTimeOffset(value, out var date)
            ? date
            : DateTimeOffset.UtcNow;
    }

    private void _SetDateCreated(DateTimeOffset createdDate)
    {
        _decodedMetadata[CreatedDateKey] = createdDate.ToString("O");
    }

    private DateTimeOffset? _GetExpirationDate()
    {
        return _decodedMetadata.TryGetValue(ExpirationKey, out var value) && _ParseDateTimeOffset(value, out var date)
            ? date
            : null;
    }

    private void _SetExpirationDate(DateTimeOffset? expirationDate)
    {
        if (expirationDate.HasValue)
        {
            _decodedMetadata[ExpirationKey] = expirationDate.Value.ToString("O");
        }
        else
        {
            _decodedMetadata.Remove(ExpirationKey);
        }
    }

    private void _SetUploadLength(long? uploadLength)
    {
        if (uploadLength.HasValue)
        {
            _decodedMetadata[UploadLengthKey] = uploadLength.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _decodedMetadata.Remove(UploadLengthKey);
        }
    }

    private long? _GetUploadLength()
    {
        return
            _decodedMetadata.TryGetValue(UploadLengthKey, out var value)
            && long.TryParse(value, CultureInfo.InvariantCulture, out var length)
            ? length
            : 0;
    }

    private void _SetBlockCount(int blockCount)
    {
        _decodedMetadata[BlockCountKey] = blockCount.ToString(CultureInfo.InvariantCulture);
    }

    private int _GetBlockCount()
    {
        return
            _decodedMetadata.TryGetValue(BlockCountKey, out var value)
            && int.TryParse(value, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    #endregion

    #region From/To Converters

    public IDictionary<string, string> ToAzure() => _decodedMetadata;

    public static TusAzureMetadata FromAzure(IDictionary<string, string> metadata) => new(metadata);

    public string ToTusString()
    {
        if (_decodedMetadata.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (var (key, value) in _decodedMetadata)
        {
            if (!key.StartsWith(TusMetadataPrefix, StringComparison.Ordinal) || _IsSystemMetadataKey(key))
            {
                continue; // Skip non-tus metadata and system keys
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(CultureInfo.InvariantCulture, $"{key[TusMetadataPrefix.Length..]} {value.ToBase64()}");
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
            result[_SanitizeAzureMetadataKey($"{TusMetadataPrefix}{key}")] = value.GetString(Encoding.UTF8);
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
                or BlockCountKey
                or ConcatTypeKey
                or PartialUploadsKey;
    }

    #endregion
}
