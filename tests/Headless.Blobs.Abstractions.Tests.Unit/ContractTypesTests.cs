// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ContractTypesTests : TestBase
{
    #region BlobQuery Tests

    [Fact]
    public void should_default_optional_values_when_only_container_provided()
    {
        // Act
        var query = new BlobQuery("bucket");

        // Assert
        query.Prefix.Should().BeNull();
        query.PageSize.Should().Be(100);
        query.ContinuationToken.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_query_container_is_null_or_blank(string? container)
    {
        // Act
        var act = () => new BlobQuery(container!);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("container");
    }

    [Theory]
    [InlineData("../escape/")]
    [InlineData("..\\escape")]
    [InlineData("nested/../escape")]
    public void should_throw_when_query_prefix_contains_traversal(string prefix)
    {
        // Act
        var act = () => new BlobQuery("bucket", prefix);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("prefix");
    }

    [Fact]
    public void should_throw_when_query_prefix_is_sidecar_suffix()
    {
        // Act
        var act = () => new BlobQuery("bucket", "report.hlmeta");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void should_throw_when_query_page_size_is_not_positive(int pageSize)
    {
        // Act
        var act = () => new BlobQuery("bucket", pageSize: pageSize);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region BlobBulkResult Tests

    [Fact]
    public void should_carry_raw_identity_without_location_when_input_path_is_invalid()
    {
        // Arrange
        var error = new InvalidOperationException("Invalid path.");

        // Act
        var result = new BlobBulkResult("bucket", "../escape.txt", Result<bool, Exception>.Fail(error));

        // Assert
        result.Container.Should().Be("bucket");
        result.Path.Should().Be("../escape.txt");
        result.Location.Should().BeNull();
        result.Result.IsFailure.Should().BeTrue();
        result.Result.Error.Should().BeSameAs(error);
    }

    #endregion
}
