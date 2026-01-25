// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.Redis;
using NSubstitute;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisBlobStorageOptionsValidatorTests
{
    private readonly RedisBlobStorageOptionsValidator _sut = new();

    private static RedisBlobStorageOptions _CreateValidOptions() =>
        new()
        {
            ConnectionMultiplexer = Substitute.For<IConnectionMultiplexer>(),
            MaxBulkParallelism = 10,
            MaxBlobSizeBytes = 10 * 1024 * 1024,
        };

    [Fact]
    public void should_pass_for_valid_options()
    {
        // given
        var options = _CreateValidOptions();

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_connection_multiplexer_is_null()
    {
        // given
        var options = _CreateValidOptions();
        options.ConnectionMultiplexer = null!;

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ConnectionMultiplexer");
    }

    [Fact]
    public void should_fail_when_max_bulk_parallelism_is_zero()
    {
        // given
        var options = _CreateValidOptions();
        options.MaxBulkParallelism = 0;

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxBulkParallelism");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(int.MinValue)]
    public void should_fail_when_max_bulk_parallelism_is_negative(int parallelism)
    {
        // given
        var options = _CreateValidOptions();
        options.MaxBulkParallelism = parallelism;

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxBulkParallelism");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(long.MinValue)]
    public void should_fail_when_max_blob_size_bytes_is_negative(long maxSize)
    {
        // given
        var options = _CreateValidOptions();
        options.MaxBlobSizeBytes = maxSize;

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "MaxBlobSizeBytes");
    }

    [Fact]
    public void should_pass_when_max_blob_size_bytes_is_zero()
    {
        // given
        var options = _CreateValidOptions();
        options.MaxBlobSizeBytes = 0;

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }
}
