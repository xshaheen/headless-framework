// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins R7 for the host source: an empty template list fails startup validation, an unparsable
/// template fails with the parser's message surfaced verbatim (so the operator sees which template
/// and which rule), and multiple valid templates pass.
/// </summary>
public sealed class TenantIdentifierSourceOptionsValidatorTests : TestBase
{
    private readonly HostTenantIdentifierSourceOptionsValidator _sut = new();

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
}
