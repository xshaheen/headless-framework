// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.FileSystem;

namespace Tests;

public sealed class FileSystemBlobStorageOptionsValidatorTests
{
    private readonly FileSystemBlobStorageOptionsValidator _sut = new();

    [Fact]
    public void should_fail_when_base_directory_path_is_null()
    {
        // Given
        var options = new FileSystemBlobStorageOptions { BaseDirectoryPath = null! };

        // When
        var result = _sut.Validate(options);

        // Then
        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle()
            .Which.PropertyName.Should()
            .Be(nameof(FileSystemBlobStorageOptions.BaseDirectoryPath));
    }

    [Fact]
    public void should_fail_when_base_directory_path_is_empty()
    {
        // Given
        var options = new FileSystemBlobStorageOptions { BaseDirectoryPath = string.Empty };

        // When
        var result = _sut.Validate(options);

        // Then
        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle()
            .Which.PropertyName.Should()
            .Be(nameof(FileSystemBlobStorageOptions.BaseDirectoryPath));
    }

    [Fact]
    public void should_fail_when_base_directory_path_is_whitespace()
    {
        // Given
        var options = new FileSystemBlobStorageOptions { BaseDirectoryPath = "   " };

        // When
        var result = _sut.Validate(options);

        // Then
        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .ContainSingle()
            .Which.PropertyName.Should()
            .Be(nameof(FileSystemBlobStorageOptions.BaseDirectoryPath));
    }

    [Fact]
    public void should_pass_for_valid_path()
    {
        // Given
        var options = new FileSystemBlobStorageOptions { BaseDirectoryPath = "/var/blobs" };

        // When
        var result = _sut.Validate(options);

        // Then
        result.IsValid.Should().BeTrue();
    }
}
