// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Models;
using tusdotnet.Models;

namespace Tests.Azure;

public sealed class TusAzureMetadataTests : TestBase
{
    #region FromTus - Parsing

    [Fact]
    public void should_store_raw_metadata_verbatim()
    {
        // given - base64 encoded values: "test.txt" -> "dGVzdC50eHQ=", "value1" -> "dmFsdWUx"
        const string tusString = "filename dGVzdC50eHQ=,custom dmFsdWUx";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then - the raw string is stored untouched in a single metadata entry
        var azure = metadata.ToAzure();
        azure.Should().ContainSingle();
        azure.Should().ContainKey(TusAzureMetadata.RawMetadataKey).WhoseValue.Should().Be(tusString);
    }

    [Fact]
    public void should_decode_base64_values_via_to_user()
    {
        // given - "Hello World!" base64 = "SGVsbG8gV29ybGQh"
        const string tusString = "message SGVsbG8gV29ybGQh";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        var user = metadata.ToUser();
        user.Should().ContainKey("message").WhoseValue.Should().Be("Hello World!");
    }

    [Fact]
    public void should_handle_empty_metadata_string()
    {
        // when
        var metadata = TusAzureMetadata.FromTus(string.Empty);

        // then
        metadata.ToAzure().Should().BeEmpty();
        metadata.ToUser().Should().BeEmpty();
    }

    [Fact]
    public void should_handle_null_metadata_string()
    {
        // when
        var metadata = TusAzureMetadata.FromTus(null);

        // then
        metadata.ToAzure().Should().BeEmpty();
        metadata.ToUser().Should().BeEmpty();
    }

    [Fact]
    public void should_preserve_original_key_casing()
    {
        // given - TUS metadata keys are case-sensitive and must round-trip as the client sent them
        const string tusString = "FileName dGVzdC50eHQ=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        metadata.ToTusString().Should().Be(tusString);
        metadata.ToUser().Should().ContainKey("FileName");
    }

    [Fact]
    public void should_preserve_keys_with_special_characters()
    {
        // given - dashes are legal in TUS keys and must not be rewritten
        const string tusString = "key-with-dashes dmFsdWU=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        metadata.ToTusString().Should().Be(tusString);
        metadata.ToUser().Should().ContainKey("key-with-dashes");
    }

    [Fact]
    public void should_preserve_keys_starting_with_number()
    {
        // given
        const string tusString = "123key dmFsdWU=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        metadata.ToTusString().Should().Be(tusString);
        metadata.ToUser().Should().ContainKey("123key");
    }

    [Fact]
    public void should_support_non_ascii_metadata_values()
    {
        // given - an Arabic filename; the VALUE bytes are arbitrary UTF-8, but the raw TUS string
        // itself stays ASCII (base64), so it is storable as an Azure metadata value
        const string arabicFileName = "ملف.pdf";
        var tusString = $"filename {arabicFileName.ToBase64()}";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        metadata.ToTusString().Should().Be(tusString);
        metadata.ToUser().Should().ContainKey("filename").WhoseValue.Should().Be(arabicFileName);
    }

    [Fact]
    public void should_allow_user_keys_matching_system_key_names()
    {
        // given - a user key that matches a reserved tus_* blob-metadata key; user metadata lives
        // inside a single raw value now, so it can never overwrite the store's tracking keys
        var tusString = $"{TusAzureMetadata.ExpirationKey} dmFsdWU=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);
        metadata.DateExpiration.Should().BeNull(); // system state untouched

        // then
        metadata.ToTusString().Should().Be(tusString);
        metadata.ToUser().Should().ContainKey(TusAzureMetadata.ExpirationKey);
    }

    [Fact]
    public void should_handle_valueless_keys()
    {
        // given - the TUS spec allows keys without a value ("is_confidential")
        const string tusString = "is_confidential";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        metadata.ToTusString().Should().Be(tusString);
        metadata.ToUser().Should().ContainKey("is_confidential").WhoseValue.Should().BeEmpty();
    }

    [Fact]
    public void should_reject_invalid_metadata_string()
    {
        // given - a value that is not valid base64
        const string tusString = "filename not!!base64";

        // when
        var act = () => TusAzureMetadata.FromTus(tusString);

        // then
        act.Should().Throw<tusdotnet.Models.TusStoreException>();
    }

    [Fact]
    public void should_reject_non_ascii_metadata_string()
    {
        // given - a raw string containing non-ASCII characters (invalid per the TUS spec and not
        // storable as an Azure metadata value)
        const string tusString = "ملف dmFsdWU=";

        // when
        var act = () => TusAzureMetadata.FromTus(tusString);

        // then
        act.Should().Throw<tusdotnet.Models.TusStoreException>().WithMessage("*ASCII*");
    }

    [Fact]
    public void should_reject_oversized_metadata_string()
    {
        // given - a raw string beyond the Azure 8 KB blob-metadata budget
        var bigValue = Convert.ToBase64String(new byte[6 * 1024]);
        var tusString = $"blob {bigValue}";

        // when
        var act = () => TusAzureMetadata.FromTus(tusString);

        // then
        act.Should().Throw<tusdotnet.Models.TusStoreException>().WithMessage("*too large*");
    }

    #endregion

    #region ToAzure/FromAzure Conversion

    [Fact]
    public void should_convert_to_azure_dictionary()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filename"] = "test.txt",
            ["custom"] = "value",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var azure = metadata.ToAzure();

        // then
        azure.Should().BeSameAs(dict);
        azure.Should().HaveCount(2);
        azure["filename"].Should().Be("test.txt");
        azure["custom"].Should().Be("value");
    }

    #endregion

    #region ToUser Conversion

    [Fact]
    public void should_convert_to_user_dictionary_excluding_system_keys()
    {
        // given - user metadata plus the store's own tracking keys on the same blob
        var customValue = "custom_value".ToBase64();
        var anotherValue = "data".ToBase64();
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.RawMetadataKey] = $"custom_key {customValue},another {anotherValue}",
            [TusAzureMetadata.UploadLengthKey] = "12345",
            [TusAzureMetadata.ExpirationKey] = "2024-01-01T00:00:00Z",
            [TusAzureMetadata.CreatedDateKey] = "2024-01-01T00:00:00Z",
            [TusAzureMetadata.ConcatTypeKey] = "final",
            [TusAzureMetadata.PartialUploadsKey] = "id1,id2",
            [TusAzureMetadata.LastChunkBlocksKey] = "block1,block2",
            [TusAzureMetadata.LastChunkChecksumKey] = "abc123",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var user = metadata.ToUser();

        // then - only the pairs from the raw Upload-Metadata value are surfaced
        user.Should().HaveCount(2);
        user.Should().ContainKey("custom_key").WhoseValue.Should().Be("custom_value");
        user.Should().ContainKey("another").WhoseValue.Should().Be("data");
    }

    [Fact]
    public void should_return_empty_user_dictionary_when_only_system_keys()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.UploadLengthKey] = "12345",
            [TusAzureMetadata.CreatedDateKey] = "2024-01-01T00:00:00Z",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var user = metadata.ToUser();

        // then
        user.Should().BeEmpty();
    }

    #endregion

    #region ToTusString Conversion

    [Fact]
    public void should_return_raw_metadata_verbatim()
    {
        // given - mixed-case and dashed keys must round-trip byte-for-byte (HEAD must echo
        // Upload-Metadata "as specified by the Client")
        const string tusString = "FileName dGVzdC50eHQ=,x-custom dmFsdWUx";
        var metadata = TusAzureMetadata.FromTus(tusString);

        // when / then
        metadata.ToTusString().Should().Be(tusString);
    }

    [Fact]
    public void should_exclude_system_keys_from_tus_string()
    {
        // given - system tracking keys live next to the raw value on the same blob
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.RawMetadataKey] = "custom dmFsdWU=",
            [TusAzureMetadata.UploadLengthKey] = "12345",
            [TusAzureMetadata.CreatedDateKey] = "2024-01-01T00:00:00Z",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var tusString = metadata.ToTusString();

        // then
        tusString.Should().Be("custom dmFsdWU=");
    }

    [Fact]
    public void should_return_empty_string_when_no_metadata()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var tusString = metadata.ToTusString();

        // then
        tusString.Should().BeEmpty();
    }

    [Fact]
    public void should_return_empty_string_when_only_system_keys()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.UploadLengthKey] = "12345",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var tusString = metadata.ToTusString();

        // then
        tusString.Should().BeEmpty();
    }

    #endregion

    #region ToTus Conversion

    [Fact]
    public void should_convert_to_tus_metadata_dictionary()
    {
        // given
        var metadata = TusAzureMetadata.FromTus($"filename {"test.txt".ToBase64()}");

        // when
        var tus = metadata.ToTus();

        // then
        tus.Should().ContainKey("filename");
        tus["filename"].GetString(Encoding.UTF8).Should().Be("test.txt");
    }

    [Fact]
    public void should_throw_when_stored_metadata_is_corrupted()
    {
        // given - blob metadata mutated out-of-band into something unparseable
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.RawMetadataKey] = "broken not!!base64",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var act = () => metadata.ToTus();

        // then - fail loudly instead of silently dropping the client's metadata
        act.Should().Throw<tusdotnet.Models.TusStoreException>().WithMessage("*corrupted*");
    }

    #endregion

    #region Round-Trip Conversion

    [Fact]
    public void should_round_trip_from_tus_to_tus_string_byte_for_byte()
    {
        // given - key order, casing, and separators must all survive the round-trip
        const string originalTusString = "ZFileName dGVzdC50eHQ=,a-custom dmFsdWUx,is_confidential";

        // when
        var metadata = TusAzureMetadata.FromTus(originalTusString);

        // then
        metadata.ToTusString().Should().Be(originalTusString);
    }

    #endregion

    #region DateCreated Property

    [Fact]
    public void should_set_date_created()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);
        var date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

        // when
        metadata.DateCreated = date;

        // then
        metadata.DateCreated.Should().Be(date);
        dict.Should().ContainKey(TusAzureMetadata.CreatedDateKey);
    }

    [Fact]
    public void should_get_date_created()
    {
        // given
        var date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.CreatedDateKey] = date.ToString("O"),
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.DateCreated;

        // then
        result.Should().Be(date);
    }

    [Fact]
    public void should_return_null_when_date_created_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.DateCreated;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_date_created_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.CreatedDateKey] = DateTimeOffset.UtcNow.ToString("O"),
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.DateCreated = null;

        // then
        metadata.DateCreated.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.CreatedDateKey);
    }

    [Fact]
    public void should_return_null_for_invalid_date_created_format()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.CreatedDateKey] = "invalid-date",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.DateCreated;

        // then
        result.Should().BeNull();
    }

    #endregion

    #region UploadLength Property

    [Fact]
    public void should_set_upload_length()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.UploadLength = 12345L;

        // then
        metadata.UploadLength.Should().Be(12345L);
        dict.Should().ContainKey(TusAzureMetadata.UploadLengthKey);
        dict[TusAzureMetadata.UploadLengthKey].Should().Be("12345");
    }

    [Fact]
    public void should_get_upload_length()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.UploadLengthKey] = "67890",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.UploadLength;

        // then
        result.Should().Be(67890L);
    }

    [Fact]
    public void should_return_null_when_upload_length_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.UploadLength;

        // then: unknown length must be null (distinct from a zero-byte upload) for the defer-length flow
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_upload_length_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.UploadLengthKey] = "12345",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.UploadLength = null;

        // then
        dict.Should().NotContainKey(TusAzureMetadata.UploadLengthKey);
    }

    [Fact]
    public void should_return_null_for_negative_upload_length()
    {
        // given - tusdotnet's Creation-Defer-Length sentinel (-1) persisted by an older version
        var dict = new Dictionary<string, string>(StringComparer.Ordinal) { [TusAzureMetadata.UploadLengthKey] = "-1" };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.UploadLength;

        // then: a negative length must read as "unknown" so HEAD reports Upload-Defer-Length,
        // not "Upload-Length: -1", and the too-much-data guard stays inert
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_for_invalid_upload_length()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.UploadLengthKey] = "not-a-number",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.UploadLength;

        // then
        result.Should().BeNull();
    }

    #endregion

    #region DateExpiration Property

    [Fact]
    public void should_set_expiration()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);
        var expiration = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);

        // when
        metadata.DateExpiration = expiration;

        // then
        metadata.DateExpiration.Should().Be(expiration);
        dict.Should().ContainKey(TusAzureMetadata.ExpirationKey);
    }

    [Fact]
    public void should_get_expiration()
    {
        // given
        var expiration = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.ExpirationKey] = expiration.ToString("O"),
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.DateExpiration;

        // then
        result.Should().Be(expiration);
    }

    [Fact]
    public void should_return_null_when_expiration_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.DateExpiration;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_expiration_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.ExpirationKey] = DateTimeOffset.UtcNow.ToString("O"),
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.DateExpiration = null;

        // then
        metadata.DateExpiration.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.ExpirationKey);
    }

    [Fact]
    public void should_return_null_for_invalid_expiration_format()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.ExpirationKey] = "invalid-date",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.DateExpiration;

        // then
        result.Should().BeNull();
    }

    #endregion

    #region ConcatType Property

    [Fact]
    public void should_set_concat_type()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.ConcatType = "final";

        // then
        metadata.ConcatType.Should().Be("final");
        dict.Should().ContainKey(TusAzureMetadata.ConcatTypeKey);
    }

    [Fact]
    public void should_get_concat_type()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.ConcatTypeKey] = "partial",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.ConcatType;

        // then
        result.Should().Be("partial");
    }

    [Fact]
    public void should_return_null_when_concat_type_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.ConcatType;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_concat_type_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.ConcatTypeKey] = "final",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.ConcatType = null;

        // then
        metadata.ConcatType.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.ConcatTypeKey);
    }

    #endregion

    #region PartialUploads Property

    [Fact]
    public void should_set_partial_uploads()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.PartialUploads = ["id1", "id2", "id3"];

        // then
        metadata.PartialUploads.Should().BeEquivalentTo(["id1", "id2", "id3"]);
        dict[TusAzureMetadata.PartialUploadsKey].Should().Be("id1,id2,id3");
    }

    [Fact]
    public void should_get_partial_uploads()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.PartialUploadsKey] = "upload1,upload2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.PartialUploads;

        // then
        result.Should().BeEquivalentTo(["upload1", "upload2"]);
    }

    [Fact]
    public void should_return_null_when_partial_uploads_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.PartialUploads;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_partial_uploads_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.PartialUploadsKey] = "id1,id2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.PartialUploads = null;

        // then
        metadata.PartialUploads.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.PartialUploadsKey);
    }

    [Fact]
    public void should_remove_partial_uploads_when_set_to_empty_array()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.PartialUploadsKey] = "id1,id2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.PartialUploads = [];

        // then
        dict.Should().NotContainKey(TusAzureMetadata.PartialUploadsKey);
    }

    #endregion

    #region LastChunkBlocks Property

    [Fact]
    public void should_set_last_chunk_blocks_as_constant_size_triple()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkBlocks = new TusStagedBlocks("1a2b3c4d", FirstIndex: 3, Count: 250_000);

        // then - the serialized value stays constant-size regardless of the block count, so the
        // tracking can never approach Azure's 8 KB blob-metadata cap
        metadata.LastChunkBlocks.Should().Be(new TusStagedBlocks("1a2b3c4d", 3, 250_000));
        dict[TusAzureMetadata.LastChunkBlocksKey].Should().Be("1a2b3c4d:3:250000");
    }

    [Fact]
    public void should_get_last_chunk_blocks()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = "deadbeef:0:3",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.LastChunkBlocks;

        // then
        result.Should().Be(new TusStagedBlocks("deadbeef", 0, 3));
    }

    [Fact]
    public void should_return_null_when_last_chunk_blocks_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.LastChunkBlocks;

        // then
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("token:1")]
    [InlineData("token:1:2:3")]
    [InlineData(":1:2")]
    [InlineData("token:x:2")]
    [InlineData("token:1:x")]
    [InlineData("token:-1:2")]
    [InlineData("token:1:0")]
    [InlineData("token:1:-2")]
    public void should_return_null_when_last_chunk_blocks_is_malformed(string storedValue)
    {
        // given - corrupted metadata must degrade to "no staged chunk" rather than throw
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = storedValue,
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.LastChunkBlocks;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_last_chunk_blocks_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = "deadbeef:0:2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkBlocks = null;

        // then
        metadata.LastChunkBlocks.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkBlocksKey);
    }

    [Fact]
    public void should_remove_last_chunk_blocks_when_set_to_empty_range()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = "deadbeef:0:2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkBlocks = new TusStagedBlocks("deadbeef", 0, Count: 0);

        // then
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkBlocksKey);
    }

    #endregion

    #region FromTus Guard Rails

    [Fact]
    public void should_reject_metadata_with_non_ascii_characters()
    {
        // given - the raw Upload-Metadata string is persisted as a single Azure metadata value,
        // which must be ASCII; a spec-conforming header (ASCII keys + base64 values) always is.
        // tusdotnet's parser only validates structure and base64 values, so a non-ASCII KEY
        // parses fine and must be stopped by the store's own guard.
        const string metadata = "filename w6ZibGVy,café dmFsdWU=";

        // when
        var act = () => TusAzureMetadata.FromTus(metadata);

        // then
        act.Should().Throw<TusStoreException>().WithMessage("*printable ASCII*");
    }

    [Fact]
    public void should_reject_metadata_with_control_characters()
    {
        // given
        const string metadata = "filename\tdmFsdWU=";

        // when
        var act = () => TusAzureMetadata.FromTus(metadata);

        // then
        act.Should().Throw<TusStoreException>().WithMessage("*printable ASCII*");
    }

    [Fact]
    public void should_reject_oversized_metadata()
    {
        // given - Azure caps total blob metadata at 8 KB; the store reserves headroom for its own
        // tus_* keys and rejects raw Upload-Metadata above ~7 KB with an actionable message
        var oversized = $"filename {Convert.ToBase64String("x"u8.ToArray())},k {new string('A', 7 * 1024)}";

        // when
        var act = () => TusAzureMetadata.FromTus(oversized);

        // then
        act.Should().Throw<TusStoreException>().WithMessage("*too large*");
    }

    [Fact]
    public void should_accept_metadata_at_the_size_limit()
    {
        // given - exactly 7 KB must round-trip (the guard is exclusive); the value length is a
        // multiple of 4 so it stays valid base64
        var value = new string('A', 7 * 1024 - "fnm ".Length);
        var metadata = $"fnm {value}";
        metadata.Length.Should().Be(7 * 1024);

        // when
        var result = TusAzureMetadata.FromTus(metadata);

        // then
        result.ToTusString().Should().Be(metadata);
    }

    #endregion

    #region LastChunkOffset Property

    [Fact]
    public void should_set_and_get_last_chunk_offset()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkOffset = 4_096L;

        // then
        metadata.LastChunkOffset.Should().Be(4_096L);
        dict.Should().ContainKey(TusAzureMetadata.LastChunkOffsetKey).WhoseValue.Should().Be("4096");
    }

    [Fact]
    public void should_remove_last_chunk_offset_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkOffsetKey] = "100",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkOffset = null;

        // then
        metadata.LastChunkOffset.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkOffsetKey);
    }

    [Fact]
    public void should_return_null_for_negative_or_invalid_last_chunk_offset()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkOffsetKey] = "-5",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when / then
        metadata.LastChunkOffset.Should().BeNull();

        dict[TusAzureMetadata.LastChunkOffsetKey] = "not-a-number";
        metadata.LastChunkOffset.Should().BeNull();
    }

    #endregion

    #region LastChunkChecksum Property

    [Fact]
    public void should_set_last_chunk_checksum()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkChecksum = "abc123xyz";

        // then
        metadata.LastChunkChecksum.Should().Be("abc123xyz");
        dict.Should().ContainKey(TusAzureMetadata.LastChunkChecksumKey);
    }

    [Fact]
    public void should_get_last_chunk_checksum()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkChecksumKey] = "checksum123",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.LastChunkChecksum;

        // then
        result.Should().Be("checksum123");
    }

    [Fact]
    public void should_return_null_when_last_chunk_checksum_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.LastChunkChecksum;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_last_chunk_checksum_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkChecksumKey] = "checksum",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkChecksum = null;

        // then
        metadata.LastChunkChecksum.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkChecksumKey);
    }

    [Fact]
    public void should_remove_last_chunk_checksum_when_set_to_empty()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkChecksumKey] = "checksum",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkChecksum = string.Empty;

        // then
        metadata.LastChunkChecksum.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkChecksumKey);
    }

    #endregion

    #region Constants

    [Fact]
    public void should_have_expected_constant_values()
    {
        TusAzureMetadata.UploadLengthKey.Should().Be("tus_upload_length");
        TusAzureMetadata.ExpirationKey.Should().Be("tus_expiration");
        TusAzureMetadata.CreatedDateKey.Should().Be("tus_created");
        TusAzureMetadata.ConcatTypeKey.Should().Be("tus_concat_type");
        TusAzureMetadata.PartialUploadsKey.Should().Be("tus_partial_uploads");
        TusAzureMetadata.RawMetadataKey.Should().Be("tus_metadata");
        TusAzureMetadata.LastChunkBlocksKey.Should().Be("tus_last_chunk_blocks");
        TusAzureMetadata.LastChunkChecksumKey.Should().Be("tus_last_chunk_checksum");
        TusAzureMetadata.LastChunkOffsetKey.Should().Be("tus_last_chunk_offset");
    }

    #endregion
}
