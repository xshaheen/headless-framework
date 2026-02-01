// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Models;
using tusdotnet.Models;

namespace Tests.Azure;

public sealed class TusAzureMetadataTests : TestBase
{
    #region FromTus - Parsing

    [Fact]
    public void should_parse_tus_metadata_string()
    {
        // given - base64 encoded values: "test.txt" -> "dGVzdC50eHQ=", "value1" -> "dmFsdWUx"
        const string tusString = "filename dGVzdC50eHQ=,custom dmFsdWUx";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        var azure = metadata.ToAzure();
        azure.Should().ContainKey("filename").WhoseValue.Should().Be("test.txt");
        azure.Should().ContainKey("custom").WhoseValue.Should().Be("value1");
    }

    [Fact]
    public void should_handle_base64_encoded_values()
    {
        // given - "Hello World!" base64 = "SGVsbG8gV29ybGQh"
        const string tusString = "message SGVsbG8gV29ybGQh";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        var azure = metadata.ToAzure();
        azure.Should().ContainKey("message").WhoseValue.Should().Be("Hello World!");
    }

    [Fact]
    public void should_handle_empty_metadata_string()
    {
        // when
        var metadata = TusAzureMetadata.FromTus(string.Empty);

        // then
        metadata.ToAzure().Should().BeEmpty();
    }

    [Fact]
    public void should_handle_null_metadata_string()
    {
        // when
        var metadata = TusAzureMetadata.FromTus(null);

        // then
        metadata.ToAzure().Should().BeEmpty();
    }

    [Fact]
    public void should_sanitize_azure_metadata_keys_to_lowercase()
    {
        // given - Azure metadata keys must be lowercase alphanumeric
        const string tusString = "FileName dGVzdC50eHQ=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        var azure = metadata.ToAzure();
        azure.Should().ContainKey("filename");
        azure.Should().NotContainKey("FileName");
    }

    [Fact]
    public void should_sanitize_special_characters_in_keys()
    {
        // given - "key-with-dashes" should become "key_with_dashes" (lowercase)
        const string tusString = "key-with-dashes dmFsdWU=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        var azure = metadata.ToAzure();
        azure.Should().ContainKey("key_with_dashes");
    }

    [Fact]
    public void should_prefix_underscore_when_key_starts_with_number()
    {
        // given - keys starting with numbers need underscore prefix
        const string tusString = "123key dmFsdWU=";

        // when
        var metadata = TusAzureMetadata.FromTus(tusString);

        // then
        var azure = metadata.ToAzure();
        azure.Should().ContainKey("_123key");
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
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["custom_key"] = "custom_value",
            ["another"] = "data",
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

        // then
        user.Should().HaveCount(2);
        user.Should().ContainKey("custom_key").WhoseValue.Should().Be("custom_value");
        user.Should().ContainKey("another").WhoseValue.Should().Be("data");
        user.Should().NotContainKey(TusAzureMetadata.UploadLengthKey);
        user.Should().NotContainKey(TusAzureMetadata.ExpirationKey);
        user.Should().NotContainKey(TusAzureMetadata.CreatedDateKey);
        user.Should().NotContainKey(TusAzureMetadata.ConcatTypeKey);
        user.Should().NotContainKey(TusAzureMetadata.PartialUploadsKey);
        user.Should().NotContainKey(TusAzureMetadata.LastChunkBlocksKey);
        user.Should().NotContainKey(TusAzureMetadata.LastChunkChecksumKey);
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
    public void should_convert_to_tus_string()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filename"] = "test.txt",
            ["custom"] = "value1",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var tusString = metadata.ToTusString();

        // then - values should be base64 encoded
        // "test.txt" -> "dGVzdC50eHQ=", "value1" -> "dmFsdWUx"
        tusString.Should().Contain("filename dGVzdC50eHQ=");
        tusString.Should().Contain("custom dmFsdWUx");
    }

    [Fact]
    public void should_exclude_system_keys_from_tus_string()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["custom"] = "value",
            [TusAzureMetadata.UploadLengthKey] = "12345",
            [TusAzureMetadata.CreatedDateKey] = "2024-01-01T00:00:00Z",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var tusString = metadata.ToTusString();

        // then
        tusString.Should().NotContain(TusAzureMetadata.UploadLengthKey);
        tusString.Should().NotContain(TusAzureMetadata.CreatedDateKey);
        tusString.Should().Contain("custom");
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
        var dict = new Dictionary<string, string>(StringComparer.Ordinal) { ["filename"] = "test.txt" };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        Dictionary<string, Metadata> tus = metadata.ToTus();

        // then
        tus.Should().ContainKey("filename");
        tus["filename"].GetString(Encoding.UTF8).Should().Be("test.txt");
    }

    #endregion

    #region Round-Trip Conversion

    [Fact]
    public void should_round_trip_from_tus_to_tus_string_to_tus()
    {
        // given
        const string originalTusString = "filename dGVzdC50eHQ=,custom dmFsdWUx";

        // when
        var metadata = TusAzureMetadata.FromTus(originalTusString);
        var resultTusString = metadata.ToTusString();
        var resultMetadata = TusAzureMetadata.FromTus(resultTusString);

        // then
        var originalAzure = metadata.ToAzure();
        var resultAzure = resultMetadata.ToAzure();
        resultAzure["filename"].Should().Be(originalAzure["filename"]);
        resultAzure["custom"].Should().Be(originalAzure["custom"]);
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
    public void should_return_zero_when_upload_length_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.UploadLength;

        // then
        result.Should().Be(0L);
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
    public void should_return_zero_for_invalid_upload_length()
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
        result.Should().Be(0L);
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

    #region Filename Property

    [Fact]
    public void should_set_filename()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.Filename = "document.pdf";

        // then
        metadata.Filename.Should().Be("document.pdf");
        dict.Should().ContainKey(TusAzureMetadata.FileNameKey);
    }

    [Fact]
    public void should_get_filename()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.FileNameKey] = "image.png",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.Filename;

        // then
        result.Should().Be("image.png");
    }

    [Fact]
    public void should_return_null_when_filename_not_set()
    {
        // given
        var metadata = TusAzureMetadata.FromAzure(new Dictionary<string, string>(StringComparer.Ordinal));

        // when
        var result = metadata.Filename;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_remove_filename_when_set_to_null()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.FileNameKey] = "test.txt",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.Filename = null;

        // then
        metadata.Filename.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.FileNameKey);
    }

    [Fact]
    public void should_remove_filename_when_set_to_empty()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.FileNameKey] = "test.txt",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.Filename = string.Empty;

        // then
        metadata.Filename.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.FileNameKey);
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
    public void should_set_last_chunk_blocks()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkBlocks = ["block1", "block2"];

        // then
        metadata.LastChunkBlocks.Should().BeEquivalentTo(["block1", "block2"]);
        dict[TusAzureMetadata.LastChunkBlocksKey].Should().Be("block1,block2");
    }

    [Fact]
    public void should_get_last_chunk_blocks()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = "b1,b2,b3",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        var result = metadata.LastChunkBlocks;

        // then
        result.Should().BeEquivalentTo(["b1", "b2", "b3"]);
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

    [Fact]
    public void should_return_null_when_last_chunk_blocks_is_empty_string()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = "",
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
            [TusAzureMetadata.LastChunkBlocksKey] = "b1,b2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkBlocks = null;

        // then
        metadata.LastChunkBlocks.Should().BeNull();
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkBlocksKey);
    }

    [Fact]
    public void should_remove_last_chunk_blocks_when_set_to_empty_array()
    {
        // given
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TusAzureMetadata.LastChunkBlocksKey] = "b1,b2",
        };
        var metadata = TusAzureMetadata.FromAzure(dict);

        // when
        metadata.LastChunkBlocks = [];

        // then
        dict.Should().NotContainKey(TusAzureMetadata.LastChunkBlocksKey);
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
        TusAzureMetadata.FileNameKey.Should().Be("tus_filename");
        TusAzureMetadata.LastChunkBlocksKey.Should().Be("tus_last_chunk_blocks");
        TusAzureMetadata.LastChunkChecksumKey.Should().Be("tus_last_chunk_checksum");
    }

    #endregion
}
