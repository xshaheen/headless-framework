// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Resources;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Pins the number check MVC and Minimal API binding share: a value Enum.TryParse would read as a number is refused,
/// and a name is left to the stack's own parsing.
/// </summary>
public sealed class RequestEnumTextTests : TestBase
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("-1", true)]
    [InlineData("+2", true)]
    [InlineData(" 3", true)]
    [InlineData("Read,2", true)]
    [InlineData("Read, -4", true)]
    [InlineData("Active", false)]
    [InlineData(" Active ", false)]
    [InlineData("Read, Write", false)]
    [InlineData("on-hold", false)]
    [InlineData("", false)]
    public void should_report_a_value_enum_parsing_would_read_as_a_number(string value, bool expected)
    {
        RequestEnumText.IsNumeric(value).Should().Be(expected);
    }

    [Fact]
    public void should_check_a_value_without_allocating()
    {
        var numeric = 0;

        for (var i = 0; i < 1_000; i++)
        {
            numeric += RequestEnumText.IsNumeric("Read, Write") ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1_000; i++)
        {
            numeric += RequestEnumText.IsNumeric("Read, Write") ? 1 : 0;
            numeric += RequestEnumText.IsNumeric("Read,2") ? 1 : 0;
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
        numeric.Should().Be(1_000, "only the list with a number is numeric");
    }

    [Fact]
    public void should_name_the_parameter_in_the_invalid_parameter_value_error()
    {
        var error = GeneralMessageDescriber.InvalidParameterValue("status");

        error.Code.Should().Be("g:invalid_parameter_value");
        error.Description.Should().Contain("status");
        error.Params.Should().ContainKey("parameter").WhoseValue.Should().Be("status");
    }
}
