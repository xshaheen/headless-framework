// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Blobs.SshNet;
using Renci.SshNet;

namespace Tests;

public sealed class SshBlobStorageOptionsValidatorTests
{
    private readonly SshBlobStorageOptionsValidator _sut = new();

    [Fact]
    public void should_fail_when_connection_string_is_empty()
    {
        // given
        var options = _CreateValidOptions();
        options.ConnectionString = string.Empty;

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.ConnectionString);
    }

    [Fact]
    public void should_fail_when_connection_string_is_null()
    {
        // given
        var options = _CreateValidOptions();
        options.ConnectionString = null!;

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.ConnectionString);
    }

    [Fact]
    public void should_fail_when_proxy_type_is_invalid()
    {
        // given
        var options = _CreateValidOptions();
        options.ProxyType = (ProxyTypes)999;

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.ProxyType);
    }

    [Fact]
    public void should_fail_when_max_concurrent_ops_is_zero()
    {
        // given
        var options = _CreateValidOptions();
        options.MaxConcurrentOperations = 0;

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.MaxConcurrentOperations);
    }

    [Fact]
    public void should_fail_when_max_concurrent_ops_exceeds_100()
    {
        // given
        var options = _CreateValidOptions();
        options.MaxConcurrentOperations = 101;
        options.MaxPoolSize = 101;

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldHaveValidationErrorFor(x => x.MaxConcurrentOperations);
    }

    [Fact]
    public void should_fail_when_max_pool_size_less_than_concurrent_ops()
    {
        // given
        var options = _CreateValidOptions();
        options.MaxConcurrentOperations = 10;
        options.MaxPoolSize = 5;

        // when
        var result = _sut.TestValidate(options);

        // then
        result
            .ShouldHaveValidationErrorFor(x => x.MaxPoolSize)
            .WithErrorMessage("MaxPoolSize must be >= MaxConcurrentOperations");
    }

    [Fact]
    public void should_pass_when_max_pool_size_equals_concurrent_ops()
    {
        // given
        var options = _CreateValidOptions();
        options.MaxConcurrentOperations = 10;
        options.MaxPoolSize = 10;

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldNotHaveValidationErrorFor(x => x.MaxPoolSize);
    }

    [Fact]
    public void should_pass_for_valid_options()
    {
        // given
        var options = _CreateValidOptions();

        // when
        var result = _sut.TestValidate(options);

        // then
        result.ShouldNotHaveAnyValidationErrors();
    }

    private static SshBlobStorageOptions _CreateValidOptions()
    {
        return new SshBlobStorageOptions
        {
            ConnectionString = "sftp://user:pass@localhost:22",
            ProxyType = ProxyTypes.None,
            MaxConcurrentOperations = 4,
            MaxPoolSize = 4,
        };
    }
}
