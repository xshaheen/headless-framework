// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation.TestHelper;
using Framework.Testing.Tests;
using Framework.Tus.Options;

namespace Tests.Azure;

public sealed class TusAzureStoreOptionsTests : TestBase
{
    private readonly TusAzureStoreOptionsValidator _validator = new();

    #region Default Values

    [Fact]
    public void should_default_container_name_to_tus_uploads()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.ContainerName.Should().Be("tus-uploads");
    }

    [Fact]
    public void should_default_blob_prefix_to_uploads_slash()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.BlobPrefix.Should().Be("uploads/");
    }

    [Fact]
    public void should_default_create_container_if_not_exists_to_true()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.CreateContainerIfNotExists.Should().BeTrue();
    }

    [Fact]
    public void should_default_enable_chunk_splitting_to_true()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.EnableChunkSplitting.Should().BeTrue();
    }

    [Fact]
    public void should_default_blob_max_chunk_size_to_100mb()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.BlobMaxChunkSize.Should().Be(100 * 1024 * 1024);
    }

    [Fact]
    public void should_default_blob_default_chunk_size_to_4mb()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.BlobDefaultChunkSize.Should().Be(4 * 1024 * 1024);
    }

    [Fact]
    public void should_default_lease_duration_to_infinite()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.LeaseDuration.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void should_default_container_public_access_type_to_none()
    {
        // when
        var options = new TusAzureStoreOptions();

        // then
        options.ContainerPublicAccessType.Should().Be(PublicAccessType.None);
    }

    #endregion

    #region Validation - ContainerName

    [Fact]
    public void should_require_container_name()
    {
        // given
        var options = new TusAzureStoreOptions { ContainerName = string.Empty };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.ContainerName);
    }

    [Fact]
    public void should_accept_valid_container_name()
    {
        // given
        var options = new TusAzureStoreOptions { ContainerName = "my-container" };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.ContainerName);
    }

    #endregion

    #region Validation - BlobMaxChunkSize

    [Fact]
    public void should_reject_blob_max_chunk_size_below_minimum()
    {
        // given
        var options = new TusAzureStoreOptions { BlobMaxChunkSize = 0 };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.BlobMaxChunkSize);
    }

    [Fact]
    public void should_reject_blob_max_chunk_size_above_maximum()
    {
        // given
        var options = new TusAzureStoreOptions { BlobMaxChunkSize = 101 * 1024 * 1024 };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.BlobMaxChunkSize);
    }

    [Fact]
    public void should_accept_minimum_blob_max_chunk_size()
    {
        // given
        var options = new TusAzureStoreOptions { BlobMaxChunkSize = 1 };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.BlobMaxChunkSize);
    }

    [Fact]
    public void should_accept_maximum_blob_max_chunk_size()
    {
        // given
        var options = new TusAzureStoreOptions { BlobMaxChunkSize = 100 * 1024 * 1024 };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.BlobMaxChunkSize);
    }

    #endregion

    #region Validation - LeaseDuration

    [Fact]
    public void should_accept_infinite_lease_duration()
    {
        // given
        var options = new TusAzureStoreOptions { LeaseDuration = Timeout.InfiniteTimeSpan };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.LeaseDuration);
    }

    [Fact]
    public void should_reject_lease_duration_below_15_seconds()
    {
        // given
        var options = new TusAzureStoreOptions { LeaseDuration = TimeSpan.FromSeconds(14) };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.LeaseDuration);
    }

    [Fact]
    public void should_reject_lease_duration_above_60_minutes()
    {
        // given
        var options = new TusAzureStoreOptions { LeaseDuration = TimeSpan.FromMinutes(61) };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.LeaseDuration);
    }

    [Fact]
    public void should_accept_minimum_lease_duration()
    {
        // given
        var options = new TusAzureStoreOptions { LeaseDuration = TimeSpan.FromSeconds(15) };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.LeaseDuration);
    }

    [Fact]
    public void should_accept_maximum_lease_duration()
    {
        // given
        var options = new TusAzureStoreOptions { LeaseDuration = TimeSpan.FromMinutes(60) };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.LeaseDuration);
    }

    #endregion

    #region Validation - ContainerPublicAccessType

    [Fact]
    public void should_accept_valid_public_access_types()
    {
        // given / when / then
        foreach (var accessType in Enum.GetValues<PublicAccessType>())
        {
            var options = new TusAzureStoreOptions { ContainerPublicAccessType = accessType };
            var result = _validator.TestValidate(options);
            result.ShouldNotHaveValidationErrorFor(x => x.ContainerPublicAccessType);
        }
    }

    [Fact]
    public void should_reject_invalid_public_access_type()
    {
        // given
        var options = new TusAzureStoreOptions { ContainerPublicAccessType = (PublicAccessType)999 };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.ContainerPublicAccessType);
    }

    #endregion

    #region Valid Options

    [Fact]
    public void should_pass_validation_with_default_values()
    {
        // given
        var options = new TusAzureStoreOptions();

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_pass_validation_with_custom_valid_values()
    {
        // given
        var options = new TusAzureStoreOptions
        {
            ContainerName = "custom-container",
            BlobPrefix = "custom/prefix/",
            CreateContainerIfNotExists = false,
            EnableChunkSplitting = false,
            BlobMaxChunkSize = 50 * 1024 * 1024,
            BlobDefaultChunkSize = 2 * 1024 * 1024,
            LeaseDuration = TimeSpan.FromSeconds(30),
            ContainerPublicAccessType = PublicAccessType.Blob,
        };

        // when
        var result = _validator.TestValidate(options);

        // then
        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion
}
