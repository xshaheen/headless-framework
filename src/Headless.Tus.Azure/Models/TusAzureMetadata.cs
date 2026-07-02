// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Headless.Tus.Models;

internal sealed class TusAzureMetadata
{
    public const string UploadLengthKey = "tus_upload_length";
    public const string ExpirationKey = "tus_expiration";
    public const string CreatedDateKey = "tus_created";
    public const string ConcatTypeKey = "tus_concat_type";
    public const string PartialUploadsKey = "tus_partial_uploads";
    public const string LastChunkBlocksKey = "tus_last_chunk_blocks";
    public const string LastChunkChecksumKey = "tus_last_chunk_checksum";
    public const string LastChunkOffsetKey = "tus_last_chunk_offset";

    /// <summary>
    /// Blob-metadata key holding the client's Upload-Metadata header value <em>verbatim</em>.
    /// </summary>
    /// <remarks>
    /// The TUS spec requires HEAD responses to echo Upload-Metadata "as specified by the Client",
    /// so the raw string is stored untouched in a single value instead of being exploded into
    /// per-key blob metadata: Azure metadata keys cannot represent arbitrary TUS keys (case,
    /// dashes, unicode) and Azure metadata values must be ASCII, while the raw TUS string —
    /// ASCII keys plus base64 values — always is. The decoded per-key view is derived on demand
    /// via <see cref="ToUser"/> / <see cref="ToTus"/>.
    /// </remarks>
    public const string RawMetadataKey = "tus_metadata";

    // Azure caps a blob's total metadata (all keys + values) at 8 KB. Leave headroom for the
    // store's own tus_* tracking keys and reject oversized Upload-Metadata with an actionable
    // message instead of an opaque Azure 400 at blob creation.
    private const int _MaxRawMetadataLength = 7 * 1024;

    private TusAzureMetadata(IDictionary<string, string> decodedMetadata)
    {
        _decodedMetadata = decodedMetadata;
    }

    private readonly IDictionary<string, string> _decodedMetadata;

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
        // Missing/unparseable returns null (unknown length) rather than 0; the Creation-Defer-Length flow relies on
        // "unknown" being distinct from a zero-byte upload, and 0 would otherwise trip the upload-length guard.
        // Negative values (tusdotnet's -1 defer-length sentinel) also map to null so a stray persisted -1 can
        // never resurface as a real length in HEAD responses or the too-much-data guard.
        get =>
            _decodedMetadata.TryGetValue(UploadLengthKey, out var value)
            && long.TryParse(value, CultureInfo.InvariantCulture, out var length)
            && length >= 0
                ? length
                : null;
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

    /// <summary>
    /// The committed offset immediately <em>before</em> the most recent append — the rollback
    /// point for discarding that chunk when a checksum-trailer verification fails.
    /// </summary>
    public long? LastChunkOffset
    {
        get =>
            _decodedMetadata.TryGetValue(LastChunkOffsetKey, out var value)
            && long.TryParse(value, CultureInfo.InvariantCulture, out var offset)
            && offset >= 0
                ? offset
                : null;
        set
        {
            if (value.HasValue)
            {
                _decodedMetadata[LastChunkOffsetKey] = value.Value.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                _decodedMetadata.Remove(LastChunkOffsetKey);
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

    /// <summary>The client's Upload-Metadata header value verbatim, or <see langword="null"/> when none was supplied.</summary>
    public string? RawMetadata
    {
        get => _decodedMetadata.TryGetValue(RawMetadataKey, out var value) ? value : null;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _decodedMetadata.Remove(RawMetadataKey);
            }
            else
            {
                _decodedMetadata[RawMetadataKey] = value;
            }
        }
    }

    #region From/To Converters

    public IDictionary<string, string> ToAzure() => _decodedMetadata;

    public static TusAzureMetadata FromAzure(IDictionary<string, string> metadata) => new(metadata);

    /// <summary>
    /// Returns the user-supplied metadata as decoded key/value pairs (UTF-8), with the client's
    /// original keys preserved. System <c>tus_*</c> keys are never part of the result.
    /// </summary>
    /// <exception cref="TusStoreException">thrown if the stored metadata string is corrupted</exception>
    public Dictionary<string, string> ToUser()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in ToTus())
        {
            result[key] = value.GetString(Encoding.UTF8);
        }

        return result;
    }

    /// <summary>
    /// Returns the client's Upload-Metadata header value verbatim, or <see cref="string.Empty"/>
    /// when the client supplied none — the round-trip contract for HEAD responses.
    /// </summary>
    public string ToTusString() => RawMetadata ?? string.Empty;

    /// <summary>
    /// Returns the user-supplied metadata as tusdotnet <see cref="Metadata"/> values keyed by the
    /// client's original keys.
    /// </summary>
    /// <exception cref="TusStoreException">thrown if the stored metadata string is corrupted</exception>
    public Dictionary<string, Metadata> ToTus()
    {
        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, ToTusString());

        // The raw string was validated when the upload was created, so a parse failure here means
        // the blob metadata was corrupted out-of-band; fail loudly rather than silently dropping it.
        return parseResult.Success
            ? parseResult.Metadata
            : throw new TusStoreException($"Stored TUS metadata is corrupted: {parseResult.ErrorMessage}");
    }

    public static TusAzureMetadata FromTus(string? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(metadata))
        {
            return new(result);
        }

        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, metadata);

        if (!parseResult.Success)
        {
            throw new TusStoreException(parseResult.ErrorMessage);
        }

        // The raw string is persisted as a single Azure metadata value, which must be ASCII with no
        // control characters. A spec-conforming Upload-Metadata header (ASCII keys + base64 values)
        // always satisfies this; anything else would otherwise surface as an opaque Azure 400.
        foreach (var c in metadata)
        {
            if (c > 127 || char.IsControl(c))
            {
                throw new TusStoreException("Upload-Metadata must contain only printable ASCII characters.");
            }
        }

        if (metadata.Length > _MaxRawMetadataLength)
        {
            throw new TusStoreException(
                $"Upload-Metadata is too large ({metadata.Length} characters). "
                    + $"Azure Blob Storage caps blob metadata at 8 KB; at most {_MaxRawMetadataLength} characters are supported."
            );
        }

        result[RawMetadataKey] = metadata;

        return new(result);
    }

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

    #endregion
}
