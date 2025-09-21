// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Tus.Options;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Framework.Tus.Models;

public sealed partial class TusAzureMetadata
{
    private const string _TusMetadataPrefix = "tus_";
    private const string _UploadLengthKey = "tus_upload_length";
    private const string _ExpirationKey = "tus_expiration";
    private const string _CreatedDateKey = "tus_created";
    private const string _BlockCountKey = "tus_block_count";

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

    #region Read/Write Helpers

    private DateTimeOffset _GetDateCreated()
    {
        return _decodedMetadata.TryGetValue(_CreatedDateKey, out var value) && _ParseDateTimeOffset(value, out var date)
            ? date
            : DateTimeOffset.UtcNow;
    }

    private void _SetDateCreated(DateTimeOffset createdDate)
    {
        _decodedMetadata[_CreatedDateKey] = createdDate.ToString("O");
    }

    private DateTimeOffset? _GetExpirationDate()
    {
        return _decodedMetadata.TryGetValue(_ExpirationKey, out var value) && _ParseDateTimeOffset(value, out var date)
            ? date
            : null;
    }

    private void _SetExpirationDate(DateTimeOffset? expirationDate)
    {
        if (expirationDate.HasValue)
        {
            _decodedMetadata[_ExpirationKey] = expirationDate.Value.ToString("O");
        }
        else
        {
            _decodedMetadata.Remove(_ExpirationKey);
        }
    }

    private void _SetUploadLength(long? uploadLength)
    {
        if (uploadLength.HasValue)
        {
            _decodedMetadata[_UploadLengthKey] = uploadLength.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _decodedMetadata.Remove(_UploadLengthKey);
        }
    }

    private long? _GetUploadLength()
    {
        return
            _decodedMetadata.TryGetValue(_UploadLengthKey, out var value)
            && long.TryParse(value, CultureInfo.InvariantCulture, out var length)
            ? length
            : 0;
    }

    private void _SetBlockCount(int blockCount)
    {
        _decodedMetadata[_BlockCountKey] = blockCount.ToString(CultureInfo.InvariantCulture);
    }

    private int _GetBlockCount()
    {
        return
            _decodedMetadata.TryGetValue(_BlockCountKey, out var value)
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
            if (!key.StartsWith(_TusMetadataPrefix, StringComparison.Ordinal) || _IsSystemMetadataKey(key))
            {
                continue; // Skip non-tus metadata and system keys
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(CultureInfo.InvariantCulture, $"{key[_TusMetadataPrefix.Length..]} {value.ToBase64()}");
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
            result[_SanitizeAzureMetadataKey($"{_TusMetadataPrefix}{key}")] = value.GetString(Encoding.UTF8);
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
        return key is _UploadLengthKey or _ExpirationKey or _CreatedDateKey or _BlockCountKey;
    }

    #endregion
}
