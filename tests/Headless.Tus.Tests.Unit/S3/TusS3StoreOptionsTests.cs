// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using FluentValidation.TestHelper;
using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests.S3;

public sealed class TusS3StoreOptionsTests : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly TusS3StoreOptionsValidator _validator = new();

    private static TusS3StoreOptions _Valid() => new() { Bucket = "uploads-bucket" };

    #region Defaults

    [Fact]
    public void should_pass_validation_with_defaults_and_a_bucket()
    {
        // given
        var options = _Valid();

        // then
        options.Prefix.Should().Be("uploads/");
        options.Provider.Should().Be(TusS3Provider.Aws);
        options.PartSize.Should().Be(8 * _MiB);
        options.MaxPartSize.Should().Be(64 * _MiB);
        options.MaxConcurrentPartUploads.Should().Be(2);
        options.PartClaimWindow.Should().Be(64);
        options.MaxTailSegments.Should().Be(16);
        options.PersistTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.MultipartUploadLifetime.Should().BeNull();
        _validator.TestValidate(options).ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region Validation

    [Fact]
    public void should_reject_an_empty_bucket()
    {
        // given
        var options = new TusS3StoreOptions();

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.Bucket);
    }

    [Fact]
    public void should_reject_an_empty_prefix()
    {
        // given
        var options = _Valid();
        options.Prefix = "";

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.Prefix);
    }

    [Theory]
    [InlineData((5 * _MiB) - 1, true)]
    [InlineData(5 * _MiB, false)]
    public void should_require_part_size_of_at_least_five_mib(int partSize, bool rejected)
    {
        // given
        var options = _Valid();
        options.PartSize = partSize;

        // when
        var result = _validator.TestValidate(options);

        // then
        if (rejected)
        {
            result.ShouldHaveValidationErrorFor(x => x.PartSize);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.PartSize);
        }
    }

    [Theory]
    [InlineData((1024 * _MiB) + 1, true)] // above the 1 GiB buffering cap
    [InlineData(int.MaxValue, true)] // the largest value the option can hold, still far below S3's 5 GiB
    [InlineData(1024 * _MiB, false)]
    [InlineData((8 * _MiB) - 1, true)] // below the default PartSize
    [InlineData(8 * _MiB, false)]
    public void should_bound_max_part_size_between_part_size_and_one_gib(int maxPartSize, bool rejected)
    {
        // given
        var options = _Valid();
        options.MaxPartSize = maxPartSize;

        // when
        var result = _validator.TestValidate(options);

        // then
        if (rejected)
        {
            result.ShouldHaveValidationErrorFor(x => x.MaxPartSize);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.MaxPartSize);
        }
    }

    [Fact]
    public void should_reject_max_concurrent_part_uploads_below_one()
    {
        // given
        var options = _Valid();
        options.MaxConcurrentPartUploads = 0;

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.MaxConcurrentPartUploads);
    }

    [Fact]
    public void should_reject_part_claim_window_below_one()
    {
        // given
        var options = _Valid();
        options.PartClaimWindow = 0;

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.PartClaimWindow);
    }

    [Fact]
    public void should_reject_max_tail_segments_below_one()
    {
        // given
        var options = _Valid();
        options.MaxTailSegments = 0;

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.MaxTailSegments);
    }

    [Fact]
    public void should_reject_a_non_positive_persist_timeout()
    {
        // given
        var options = _Valid();
        options.PersistTimeout = TimeSpan.Zero;

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.PersistTimeout);
    }

    [Fact]
    public void should_reject_a_non_positive_multipart_upload_lifetime()
    {
        // given
        var options = _Valid();
        options.MultipartUploadLifetime = TimeSpan.Zero;

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.MultipartUploadLifetime);
    }

    [Fact]
    public void should_reject_an_undefined_provider()
    {
        // given
        var options = _Valid();
        options.Provider = (TusS3Provider)99;

        // when / then
        _validator.TestValidate(options).ShouldHaveValidationErrorFor(x => x.Provider);
    }

    [Fact]
    public void should_validate_options_when_constructing_the_store()
    {
        // given
        var options = _Valid();
        options.PartSize = 1;

        // when
        var act = () => new TusS3Store(Substitute.For<IAmazonS3>(), options);

        // then
        act.Should().Throw<FluentValidation.ValidationException>();
    }

    #endregion

    #region Resolved values

    [Theory]
    [InlineData("uploads", "uploads/")]
    [InlineData("uploads/", "uploads/")]
    public void should_normalize_prefix_to_end_with_a_slash(string prefix, string expected)
    {
        // given
        var options = _Valid();
        options.Prefix = prefix;

        // when / then
        options.NormalizedPrefix.Should().Be(expected);
    }

    [Fact]
    public void should_resolve_r2_multipart_upload_lifetime_to_seven_days_when_unset()
    {
        // given
        var options = _Valid();
        options.Provider = TusS3Provider.R2;

        // when / then
        options.EffectiveMultipartUploadLifetime.Should().Be(TimeSpan.FromDays(7));
    }

    [Theory]
    [InlineData(TusS3Provider.Aws)]
    [InlineData(TusS3Provider.S3Compatible)]
    [InlineData(TusS3Provider.R2)]
    public void should_prefer_a_configured_multipart_upload_lifetime(TusS3Provider provider)
    {
        // given
        var options = _Valid();
        options.Provider = provider;
        options.MultipartUploadLifetime = TimeSpan.FromDays(2);

        // when / then
        options.EffectiveMultipartUploadLifetime.Should().Be(TimeSpan.FromDays(2));
    }

    [Theory]
    [InlineData(TusS3Provider.Aws)]
    [InlineData(TusS3Provider.S3Compatible)]
    public void should_assume_no_multipart_upload_lifetime_outside_r2_when_unset(TusS3Provider provider)
    {
        // given
        var options = _Valid();
        options.Provider = provider;

        // when / then
        options.EffectiveMultipartUploadLifetime.Should().BeNull();
    }

    #endregion

    #region Request presets

    [Fact]
    public void should_send_unsigned_payloads_without_chunk_encoding_for_r2()
    {
        // given
        var put = new PutObjectRequest();
        var part = new UploadPartRequest();

        // when
        TusS3RequestPresets.Apply(put, TusS3Provider.R2);
        TusS3RequestPresets.Apply(part, TusS3Provider.R2);

        // then
        put.DisablePayloadSigning.Should().BeTrue();
        put.UseChunkEncoding.Should().BeFalse();
        part.DisablePayloadSigning.Should().BeTrue();
        part.UseChunkEncoding.Should().BeFalse();
    }

    [Fact]
    public void should_send_signed_payloads_without_chunk_encoding_for_s3_compatible_servers()
    {
        // given
        var put = new PutObjectRequest();
        var part = new UploadPartRequest();

        // when
        TusS3RequestPresets.Apply(put, TusS3Provider.S3Compatible);
        TusS3RequestPresets.Apply(part, TusS3Provider.S3Compatible);

        // then
        put.DisablePayloadSigning.Should().BeFalse();
        put.UseChunkEncoding.Should().BeFalse();
        part.DisablePayloadSigning.Should().BeFalse();
        part.UseChunkEncoding.Should().BeFalse();
    }

    [Fact]
    public void should_keep_the_sdk_payload_signing_default_for_aws()
    {
        // given
        var put = new PutObjectRequest();
        var part = new UploadPartRequest();
        var defaultPutSigning = new PutObjectRequest().DisablePayloadSigning;
        var defaultPartSigning = new UploadPartRequest().DisablePayloadSigning;

        // when
        TusS3RequestPresets.Apply(put, TusS3Provider.Aws);
        TusS3RequestPresets.Apply(part, TusS3Provider.Aws);

        // then
        put.DisablePayloadSigning.Should().Be(defaultPutSigning);
        part.DisablePayloadSigning.Should().Be(defaultPartSigning);
        put.UseChunkEncoding.Should().BeFalse();
        part.UseChunkEncoding.Should().BeFalse();
    }

    #endregion
}
