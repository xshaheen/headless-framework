// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins R7 for the host source: an empty template list fails startup validation, an unparsable
/// template fails with the parser's message surfaced verbatim (so the operator sees which template
/// and which rule), and multiple valid templates pass. Also pins R7 for the route source: a blank
/// route value name fails validation while the default passes. And for the header source: an empty
/// header-name list, a blank name, or a name that is not an HTTP token fails, naming the offender.
/// </summary>
public sealed class TenantIdentifierSourceOptionsValidatorTests : TestBase
{
    private readonly HostTenantIdentifierSourceOptionsValidator _sut = new();
    private readonly RouteTenantIdentifierSourceOptionsValidator _routeSut = new();
    private readonly HeaderTenantIdentifierSourceOptionsValidator _headerSut = new();

    [Fact]
    public void should_accept_two_valid_host_templates()
    {
        var options = new HostTenantIdentifierSourceOptions { Templates = ["{tenant}.example.com", "{tenant}.dev"] };

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_reject_an_empty_host_template_list()
    {
        var options = new HostTenantIdentifierSourceOptions { Templates = [] };

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.Templates);
    }

    [Fact]
    public void should_reject_an_unparsable_host_template_with_the_parser_message()
    {
        var options = new HostTenantIdentifierSourceOptions { Templates = ["{tenant}.{tenant}.com"] };

        var result = _sut.TestValidate(options);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain("{tenant}.{tenant}.com").And.Contain("exactly one");
    }

    [Fact]
    public void should_reject_a_host_template_with_a_port()
    {
        var options = new HostTenantIdentifierSourceOptions { Templates = ["{tenant}.example.com:443"] };

        var result = _sut.TestValidate(options);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain("{tenant}.example.com:443").And.Contain("port");
    }

    [Fact]
    public void should_reject_a_null_entry_in_the_template_list()
    {
        var options = new HostTenantIdentifierSourceOptions { Templates = [null!] };

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.Templates);
    }

    // --- route source (R7) ---

    [Fact]
    public void should_accept_the_default_route_value_name()
    {
        var options = new RouteTenantIdentifierSourceOptions();

        var result = _routeSut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void should_reject_a_blank_route_value_name(string? routeValueName)
    {
        var options = new RouteTenantIdentifierSourceOptions { RouteValueName = routeValueName! };

        var result = _routeSut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.RouteValueName);
    }

    // --- header source (R7) ---

    [Fact]
    public void should_accept_the_default_header_name()
    {
        var options = new HeaderTenantIdentifierSourceOptions();

        var result = _headerSut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_accept_two_valid_header_names()
    {
        var options = new HeaderTenantIdentifierSourceOptions { HeaderNames = ["X-Tenant", "X-Legacy-Tenant"] };

        var result = _headerSut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_reject_an_empty_header_name_list()
    {
        var options = new HeaderTenantIdentifierSourceOptions { HeaderNames = [] };

        var result = _headerSut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.HeaderNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void should_reject_a_blank_header_name(string? headerName)
    {
        var options = new HeaderTenantIdentifierSourceOptions { HeaderNames = [headerName!] };

        var result = _headerSut.TestValidate(options);

        result.Errors.Should().ContainSingle();
    }

    [Theory]
    [InlineData("X Tenant")]
    [InlineData("X-Tenant:")]
    [InlineData("X-Tenant\r\n")]
    [InlineData("X\"Tenant")]
    [InlineData("X-Ténant")]
    public void should_reject_a_header_name_that_is_not_an_http_token_naming_it(string headerName)
    {
        var options = new HeaderTenantIdentifierSourceOptions { HeaderNames = [headerName] };

        var result = _headerSut.TestValidate(options);

        var failure = result.Errors.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Contain(headerName).And.Contain("token");
    }
}
